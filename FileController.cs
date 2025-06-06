using BOBDrive.Models;
using System;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Configuration;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Data.Entity; // For EntityState

namespace BOBDrive.Controllers
{
    public class FileController : BaseController
    {
        private readonly string _finalUploadPath;
        private readonly string _tempChunkPath;

        public FileController()
        {
            string fileUploadPathSetting = ConfigurationManager.AppSettings["FileUploadPath"];
            string tempChunkUploadPathSetting = ConfigurationManager.AppSettings["TempChunkUploadPath"];

            if (string.IsNullOrEmpty(fileUploadPathSetting))
                throw new ConfigurationErrorsException("Configuration Error: 'FileUploadPath' not set.");
            if (string.IsNullOrEmpty(tempChunkUploadPathSetting))
                throw new ConfigurationErrorsException("Configuration Error: 'TempChunkUploadPath' not set.");

            _finalUploadPath = System.Web.Hosting.HostingEnvironment.MapPath(fileUploadPathSetting);
            _tempChunkPath = System.Web.Hosting.HostingEnvironment.MapPath(tempChunkUploadPathSetting);

            if (_finalUploadPath == null)
                throw new ConfigurationErrorsException("Configuration Error: Could not resolve 'FileUploadPath'.");
            if (_tempChunkPath == null)
                throw new ConfigurationErrorsException("Configuration Error: Could not resolve 'TempChunkUploadPath'.");

            if (!Directory.Exists(_finalUploadPath))
                Directory.CreateDirectory(_finalUploadPath);
            if (!Directory.Exists(_tempChunkPath))
                Directory.CreateDirectory(_tempChunkPath);
        }

        // POST: /File/UploadChunk
        [HttpPost]
        public async Task<JsonResult> UploadChunk(
            HttpPostedFileBase chunk,
            string fileIdForUpload,
            int chunkNumber,
            int totalChunks,
            string originalFileName,
            int folderId,
            long totalFileSize,
            string fileContentType)
        {
            if (chunk == null || chunk.ContentLength == 0)
            {
                return Json(new { success = false, message = "Empty chunk received." });
            }

            try
            {
                // ==== 1. Check disk space before saving this chunk ====
                // Get the drive that contains _finalUploadPath:
                string driveRoot = Path.GetPathRoot(_finalUploadPath);
                DriveInfo drive = new DriveInfo(driveRoot);
                long freeSpaceBytes = drive.AvailableFreeSpace;
                if (freeSpaceBytes < chunk.ContentLength)
                {
                    // Not enough space to accept this chunk.
                    return Json(new
                    {
                        success = false,
                        errorCode = "DiskFull",
                        message = "Server: Insufficient disk space."
                    });
                }

                // ==== 2. Save chunk to a temp folder ====
                string chunkDirectory = Path.Combine(_tempChunkPath, fileIdForUpload);
                if (!Directory.Exists(chunkDirectory))
                    Directory.CreateDirectory(chunkDirectory);

                string chunkFilePath = Path.Combine(chunkDirectory, chunkNumber.ToString());
                chunk.SaveAs(chunkFilePath);

                // ==== 3. Record the chunk in the database ====
                var fileChunk = new FileChunk
                {
                    FileIdForUpload = fileIdForUpload,
                    ChunkNumber = chunkNumber,
                    ChunkFilePath = chunkFilePath,
                    UploadedAt = DateTime.UtcNow
                };
                db.FileChunks.Add(fileChunk);
                await db.SaveChangesAsync();

                // ==== 4. See if we have all chunks now ====
                int chunksUploadedCount = db.FileChunks.Count(fc => fc.FileIdForUpload == fileIdForUpload);
                if (chunksUploadedCount == totalChunks)
                {
                    // Inform client that all chunks are now on server
                    return Json(new
                    {
                        success = true,
                        allChunksUploaded = true,
                        fileIdForUpload = fileIdForUpload
                    });
                }

                // Otherwise, normal chunk‐uploaded response
                return Json(new
                {
                    success = true,
                    message = "Chunk " + (chunkNumber + 1) + " of " + totalChunks + " uploaded.",
                    allChunksUploaded = false
                });
            }
            catch (Exception ex)
            {
                // You may want to log ex
                return Json(new
                {
                    success = false,
                    message = "Error processing chunk: " + ex.Message
                });
            }
        }

        // POST: /File/CompleteUpload
        [HttpPost]
        public async Task<JsonResult> CompleteUpload(
            string fileIdForUpload,
            string originalFileName,
            int folderId,
            long totalFileSize,
            string fileContentType)
        {
            try
            {
                var chunks = db.FileChunks
                               .Where(fc => fc.FileIdForUpload == fileIdForUpload)
                               .OrderBy(fc => fc.ChunkNumber)
                               .ToList();

                if (!chunks.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "No chunks found for this file ID."
                    });
                }

                // Make a safe final filename
                string safeFileName = Path.GetFileName(originalFileName);
                string finalFilePath = Path.Combine(_finalUploadPath, safeFileName);

                int count = 1;
                string fileNameOnly = Path.GetFileNameWithoutExtension(finalFilePath);
                string extension = Path.GetExtension(finalFilePath);
                while (System.IO.File.Exists(finalFilePath))
                {
                    safeFileName = string.Format("{0}({1}){2}", fileNameOnly, count++, extension);
                    finalFilePath = Path.Combine(_finalUploadPath, safeFileName);
                }

                // 1) Create DB record (with IsProcessing = true)
                var newFile = new BOBDrive.Models.File
                {
                    Name = safeFileName,
                    ContentType = fileContentType,
                    Size = totalFileSize,
                    FilePath = finalFilePath,
                    FolderId = folderId,
                    UploadedAt = DateTime.Now,
                    IsProcessing = true
                };
                db.Files.Add(newFile);
                await db.SaveChangesAsync();

                // 2) Merge chunks into the final file
                using (var destStream = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    foreach (var chunk in chunks)
                    {
                        // Before copying each chunk, check disk space again
                        string driveRoot = Path.GetPathRoot(_finalUploadPath);
                        DriveInfo drive = new DriveInfo(driveRoot);
                        long freeSpaceBytes = drive.AvailableFreeSpace;
                        FileInfo fi = new FileInfo(chunk.ChunkFilePath);
                        long chunkSize = fi.Length;
                        if (freeSpaceBytes < chunkSize)
                        {
                            // Disk has filled up during merge; we abort and roll back
                            // Delete any partial file
                            destStream.Dispose();
                            if (System.IO.File.Exists(finalFilePath))
                                System.IO.File.Delete(finalFilePath);

                            // Remove DB record
                            db.Files.Remove(newFile);
                            await db.SaveChangesAsync();

                            return Json(new
                            {
                                success = false,
                                errorCode = "DiskFull",
                                message = "Server: Disk full during merge. Upload aborted."
                            });
                        }

                        // Copy chunk to final file
                        using (var sourceStream = new FileStream(chunk.ChunkFilePath, FileMode.Open, FileAccess.Read))
                        {
                            await sourceStream.CopyToAsync(destStream);
                        }
                        System.IO.File.Delete(chunk.ChunkFilePath);
                    }
                }

                // 3) Delete the temp chunk folder
                string chunkDirectory = Path.Combine(_tempChunkPath, fileIdForUpload);
                if (Directory.Exists(chunkDirectory))
                    Directory.Delete(chunkDirectory, true);

                // 4) Remove chunk records and mark file as no longer processing
                db.FileChunks.RemoveRange(chunks);
                newFile.IsProcessing = false;
                db.Entry(newFile).State = EntityState.Modified;
                await db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "File uploaded and merged successfully.",
                    fileName = newFile.Name
                });
            }
            catch (Exception ex)
            {
                // You may want to log ex
                return Json(new
                {
                    success = false,
                    message = "Error merging chunks: " + ex.Message
                });
            }
        }

        // GET: /File/Download/5
        public async Task<ActionResult> Download(int id)
        {
            var fileRecord = await db.Files.FindAsync(id);
            if (fileRecord == null || fileRecord.IsProcessing)
            {
                return HttpNotFound("File not found or is currently being processed.");
            }

            var filePath = fileRecord.FilePath;
            if (!System.IO.File.Exists(filePath))
            {
                return HttpNotFound("Physical file not found on server.");
            }

            FileInfo fileInfo = new FileInfo(filePath);
            long totalFileSize = fileInfo.Length;
            string rangeHeader = Request.Headers["Range"];

            Response.Clear();
            Response.BufferOutput = false;

            if (string.IsNullOrWhiteSpace(rangeHeader))
            {
                // Full download
                Response.StatusCode = (int)HttpStatusCode.OK;
                Response.AppendHeader("Accept-Ranges", "bytes");
                Response.AppendHeader("Content-Disposition",
                    new System.Net.Mime.ContentDisposition
                    {
                        FileName = fileRecord.Name,
                        Inline = false
                    }.ToString());
                Response.ContentType = fileRecord.ContentType;
                Response.AppendHeader("Content-Length", totalFileSize.ToString());

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true))
                {
                    await fileStream.CopyToAsync(Response.OutputStream);
                }
            }
            else
            {
                // Partial (Range) download
                try
                {
                    long start = 0, end = 0;
                    var rangeParts = rangeHeader.Replace("bytes=", "").Split('-');
                    string startStr = rangeParts[0];
                    string endStr = (rangeParts.Length > 1) ? rangeParts[1] : null;

                    if (string.IsNullOrWhiteSpace(startStr))
                    {
                        // format: "bytes=-<suffix-length>"
                        long suffixLength;
                        if (string.IsNullOrWhiteSpace(endStr) || !long.TryParse(endStr, out suffixLength) || suffixLength <= 0)
                            throw new FormatException("Invalid suffix length format.");
                        start = Math.Max(0, totalFileSize - suffixLength);
                        end = totalFileSize - 1;
                    }
                    else
                    {
                        if (!long.TryParse(startStr, out start) || start < 0)
                            throw new FormatException("Invalid start range format.");

                        if (string.IsNullOrWhiteSpace(endStr))
                            end = totalFileSize - 1;
                        else if (!long.TryParse(endStr, out end) || end < 0)
                            throw new FormatException("Invalid end range format.");
                    }

                    if (end >= totalFileSize) end = totalFileSize - 1;
                    if (start > end || start >= totalFileSize)
                    {
                        Response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                        Response.AppendHeader("Content-Range", "bytes */" + totalFileSize);
                    }
                    else
                    {
                        long bytesToRead = (end - start) + 1;
                        Response.StatusCode = (int)HttpStatusCode.PartialContent;
                        Response.AppendHeader("Accept-Ranges", "bytes");
                        Response.AppendHeader("Content-Range", "bytes " + start + "-" + end + "/" + totalFileSize);
                        Response.ContentType = fileRecord.ContentType;
                        Response.AppendHeader("Content-Disposition",
                            new System.Net.Mime.ContentDisposition
                            {
                                FileName = fileRecord.Name,
                                Inline = false
                            }.ToString());
                        Response.AppendHeader("Content-Length", bytesToRead.ToString());

                        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true))
                        {
                            fileStream.Seek(start, SeekOrigin.Begin);
                            byte[] buffer = new byte[65536];
                            long currentBytesSent = 0;

                            while (currentBytesSent < bytesToRead)
                            {
                                if (!Response.IsClientConnected) break;

                                int bytesToReadFromStream = (int)Math.Min(bytesToRead - currentBytesSent, buffer.Length);
                                int actualRead = await fileStream.ReadAsync(buffer, 0, bytesToReadFromStream);
                                if (actualRead == 0) break;

                                await Response.OutputStream.WriteAsync(buffer, 0, actualRead);
                                currentBytesSent += actualRead;
                            }
                        }
                    }
                }
                catch (FormatException ex)
                {
                    if (!Response.HeadersWritten)
                    {
                        Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        Response.StatusDescription = "Malformed Range header: " + ex.Message;
                    }
                }
                catch (Exception ex)
                {
                    if (!Response.HeadersWritten)
                    {
                        Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        Response.StatusDescription = "Error processing file stream.";
                    }
                }
            }

            return new EmptyResult();
        }

        // GET: /File/GetFolderContents?folderId=123
        // Returns a partial view listing subfolders and files for AJAX navigation.
        // FileController.cs
        public ActionResult GetFolderContents(int? folderId)
        {
            int actualFolderId = folderId ?? 0; // assume 0 means “root”

            // 1) Try to find the requested folder by ID.
            var folderFromDb = db.Folders.Find(actualFolderId);

            // 2) If that was null, try to find any folder with ParentFolderId == null (the “true” root).
            if (folderFromDb == null)
            {
                folderFromDb = db.Folders.FirstOrDefault(f => f.ParentFolderId == null);
            }

            // 3) If STILL null (i.e. your DB really has no folders at all), create a "virtual" root in memory:
            if (folderFromDb == null)
            {
                folderFromDb = new Folder
                {
                    Id = 0,                  // “0” just means “virtual root”
                    Name = "Root (auto‐created)",
                    ParentFolderId = (int?)null,
                    CreatedAt = DateTime.UtcNow
                };
                // **Notice**: We do NOT save it to the database—this is only to avoid nulls.
                // All subfolders & files queries below will return empty lists (because ID=0).
            }

            // 4) Now folderFromDb is non‐null. Build the ViewModel:
            var vm = new BOBDrive.ViewModels.FolderViewModel
            {
                CurrentFolder = folderFromDb,
                SubFolders = db.Folders
                                             .Where(f => f.ParentFolderId == folderFromDb.Id)
                                             .ToList(),
                Files = db.Files
                                             .Where(f => f.FolderId == folderFromDb.Id && !f.IsProcessing)
                                             .OrderByDescending(f => f.UploadedAt)
                                             .ToList(),
                ParentOfCurrentFolderId = folderFromDb.ParentFolderId
            };

            return PartialView("_FolderContentsPartial", vm);
        }


        //cancel the upload inbetween
        [HttpPost]
        public async Task<JsonResult> CancelUpload(string fileIdForUpload)
        {
            if (string.IsNullOrEmpty(fileIdForUpload))
            {
                return Json(new { success = false, message = "Invalid file ID." });
            }

            try
            {
                // 1) Find all FileChunk records for this upload ID
                var chunks = db.FileChunks
                               .Where(fc => fc.FileIdForUpload == fileIdForUpload)
                               .ToList();

                // 2) Delete the chunk files on disk
                foreach (var chunk in chunks)
                {
                    try
                    {
                        if (System.IO.File.Exists(chunk.ChunkFilePath))
                        {
                            System.IO.File.Delete(chunk.ChunkFilePath);
                        }
                    }
                    catch
                    {
                        // If a particular chunk file is locked or missing, ignore and continue
                    }
                }

                // 3) Delete the entire temp folder, in case any stray files remain
                var tempChunkPathSetting = ConfigurationManager.AppSettings["TempChunkUploadPath"];
                var tempRoot = System.Web.Hosting.HostingEnvironment.MapPath(tempChunkPathSetting);
                if (!string.IsNullOrEmpty(tempRoot))
                {
                    var chunkDirectory = Path.Combine(tempRoot, fileIdForUpload);
                    if (Directory.Exists(chunkDirectory))
                    {
                        Directory.Delete(chunkDirectory, recursive: true);
                    }
                }

                // 4) Remove the DB records
                db.FileChunks.RemoveRange(chunks);
                await db.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (System.Exception ex)
            {
                // You may want to log ex.Message somewhere
                return Json(new { success = false, message = "Error during cancellation: " + ex.Message });
            }
        }
    }
}