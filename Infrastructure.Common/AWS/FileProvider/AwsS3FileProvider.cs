using Amazon.S3.Model;
using Amazon.S3;
using Application.Common.FileProvider;
using Domain.Common.Settings;
using Microsoft.Extensions.Options;
using Utils.Common.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.FileProvider
{
    public class AwsS3FileProvider : IFileProvider
    {
        #region Private Properties

        private readonly string _invalidFileNameChars = @"[~#%&\*\{\}\(\)\\/:;<>\?\|""'\^]";

        private readonly string _invalidAsciiChars = @"[^\u0000-\u007F]+";

        private readonly IAmazonS3 _amazonS3Client;

        private readonly List<AwsTag> _defaultTags;

        private static readonly List<S3Bucket> _s3Buckets = new();

        #endregion

        public AwsS3FileProvider(IAmazonS3 amazonS3Client,
         IOptions<AwsTagsConfigSettings> tagConfigurationSettings)
        {
            _amazonS3Client = amazonS3Client;
            _defaultTags = tagConfigurationSettings.Value.DefaultTags;
        }

        #region IFileProvider Methods

        public async Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken = default)
        {
            ParseFilePath(fileName, out string bucketName, out string objectKey);
            var response = await _amazonS3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey,
            }, cancellationToken);
            return response.HttpStatusCode == HttpStatusCode.NoContent;
        }

        public async Task<UploadedFile?> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            var response = await GetFilesAsync(new List<string> { path }, cancellationToken);
            return response.FirstOrDefault();
        }

        public async Task<List<UploadedFile>> GetFilesAsync(List<string> paths, CancellationToken cancellationToken = default)
        {
            var files = new List<UploadedFile>();

            foreach (var path in paths)
            {
                ParseFilePath(path, out string bucketName, out string objectKey);
                (byte[]? data, Dictionary<string, string>? metaData) = await GetObjectAsync(bucketName, objectKey, cancellationToken);
                files.Add(new UploadedFile
                {
                    Path = path,
                    Name = objectKey,
                    MetaData = metaData,
                    Data = data
                });
            }
            return files;
        }

        public async Task<List<UploadedFile>> GetFilesAsync(string containerName, CancellationToken cancellationToken = default)
        {
            return await GetObjectListAsync(containerName, default, true, cancellationToken: cancellationToken);
        }

        public async Task<List<UploadedFile>> GetFilesAsync(string containerName, string prefix, CancellationToken cancellationToken = default)
        {
            return await GetObjectListAsync(containerName, prefix, true, cancellationToken: cancellationToken);
        }

        public async Task<List<UploadedFile>> GetListAsync(string containerName, CancellationToken cancellationToken = default)
        {
            return await GetObjectListAsync(containerName, default, cancellationToken: cancellationToken);
        }

        public async Task<List<UploadedFile>> GetListAsync(string containerName, string prefix, CancellationToken cancellationToken = default)
        {
            return await GetObjectListAsync(containerName, prefix, cancellationToken: cancellationToken);
        }

        // https://docs.aws.amazon.com/AmazonS3/latest/userguide/copy-object.html
        // Once we have created an object we cannot change its meta data we have to create a copy of that object.To do so, in the copy operation,need to set the same object as the source and target.
        public async Task<bool> SetMetaDataAsync(string path, IDictionary<string, string> metaData, CancellationToken cancellationToken = default)
        {
            ParseFilePath(path, out string bucketName, out string objectKey);
            var request = new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = objectKey,
                DestinationBucket = bucketName,
                DestinationKey = objectKey,
                MetadataDirective = S3MetadataDirective.REPLACE
            };

            foreach (var keyValuePair in metaData)
            {
                request.Metadata.Add(keyValuePair.Key, keyValuePair.Value);
            }

            var response = await _amazonS3Client.CopyObjectAsync(request, cancellationToken);
            return response.HttpStatusCode == HttpStatusCode.OK;
        }

        public async Task<string?> UploadAsync(string containerName, string prefix, string contentType, byte[] data, CancellationToken cancellationToken = default)
        {
            string? filePath = null;

            List<UploadedFile> files = new()
            {
                new UploadedFile
                {
                    Name = Guid.NewGuid().ToString() + FileInformation.GetFileExtension(contentType),
                    Data = data
                }
            };

            if (await UploadAsync(containerName, prefix, files, cancellationToken))
            {
                filePath = files.FirstOrDefault()?.Path;
            }

            return filePath;
        }

        public async Task<bool> UploadAsync(string containerName, List<UploadedFile> files, CancellationToken cancellationToken = default)
        {
            return await UploadAsync(containerName, default, files, cancellationToken);
        }

        [SuppressMessage("Maintainability", "S134", Justification = "Here we need more than 3 control flow statements so suppressing this.")]
        public async Task<bool> UploadAsync(string containerName, string? prefix, List<UploadedFile> files, CancellationToken cancellationToken = default)
        {
            // Checking for whether bucket exists.
            if (await CreateBucketIfNotExistsAsync(containerName, cancellationToken))
            {
                foreach (var file in files)
                {
                    string fileName = string.IsNullOrEmpty(file.Name) ? Guid.NewGuid().ToString() : CleanFileName(file.Name);
                    string objectKey = string.IsNullOrEmpty(prefix) ? fileName : prefix.ToLower() + "/" + fileName;

                    if (file.Data is not null)
                    {
                        using var ms = new MemoryStream(file.Data, false);
                        var request = new PutObjectRequest
                        {
                            BucketName = containerName,
                            Key = objectKey,
                            InputStream = ms,
                            TagSet = file.TtlDays.HasValue ? new List<Tag>
                            {
                               new Tag
                               {
                                    Key = "TTL",
                                    Value = file.TtlDays.Value.ToString(),
                               }
                            } : null,
                        };

                        if (file.MetaData is not null)
                        {
                            foreach (var keyValuePair in file.MetaData)
                            {
                                request.Metadata.Add(keyValuePair.Key, keyValuePair.Value);
                            }
                        }

                        var response = await _amazonS3Client.PutObjectAsync(request, cancellationToken);
                        file.Path = response.HttpStatusCode == HttpStatusCode.OK ? GetFilePath(containerName, objectKey) : string.Empty;
                    }
                }
            }
            return true;
        }

        public async Task<string> GetPresignedDownloadUrlAsync(string path, TimeSpan validFor, CancellationToken cancellationToken = default)
        {
            ParseFilePath(path, out string bucketName, out string objectKey);
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(validFor)
            };

            return await _amazonS3Client.GetPreSignedURLAsync(request);
        }

        public async Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan validFor, string? contentType = null, CancellationToken cancellationToken = default)
        {
            ParseFilePath(path, out string bucketName, out string objectKey);
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(validFor)
            };

            if (!string.IsNullOrEmpty(contentType))
            {
                request.ContentType = contentType;
            }

            return await _amazonS3Client.GetPreSignedURLAsync(request);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// This method will return the path for an object in bucket.
        /// </summary>
        /// <param name="bucketName">Name of the bucket.</param>
        /// <param name="objectKey">Key of the object in bucket.</param>
        /// <returns>Path to object in bucket.</returns>
        private static string GetFilePath(string bucketName, string objectKey)
        {
            return $"{bucketName}/{objectKey}";
        }

        private string CleanFileName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            // Replace invalid characters with empty strings.
            string returnStr = Regex.Replace(Regex.Replace(name, _invalidFileNameChars, "", RegexOptions.NonBacktracking),
                _invalidAsciiChars, "", RegexOptions.NonBacktracking);
            returnStr += ext;
            return returnStr;
        }

        /// <summary>
        /// This method will check whether bucket exists or not, if not will create it.
        /// </summary>
        /// <param name="bucket"></param>
        /// <returns>True or False</returns>
        private async Task<bool> CreateBucketIfNotExistsAsync(string bucketName, CancellationToken cancellationToken)
        {
            if (await IsBucketExistsAsync(bucketName, cancellationToken))
            {
                return true;
            }

            var response = await _amazonS3Client.PutBucketAsync(bucketName, cancellationToken);
            var defaultTags = _defaultTags.Select(t => new Tag
            {
                Key = t.Key,
                Value = t.Value
            }).ToList();

            await _amazonS3Client.PutBucketTaggingAsync(new PutBucketTaggingRequest
            {
                BucketName = bucketName,
                TagSet = defaultTags
            }, cancellationToken);
            return response.HttpStatusCode == HttpStatusCode.OK;
        }

        private async Task<bool> IsBucketExistsAsync(string bucketName, CancellationToken cancellationToken)
        {
            if (_s3Buckets.Count > 0 && _s3Buckets.Exists(x => x.BucketName == bucketName))
            {
                return true;
            }

            var listOfBuckets = await _amazonS3Client.ListBucketsAsync(cancellationToken);

            if (_s3Buckets.Count > 0)
            {
                _s3Buckets.Clear();
            }

            _s3Buckets.AddRange(listOfBuckets.Buckets);

            if (listOfBuckets.Buckets.Exists(x => x.BucketName == bucketName))
            {
                return true;
            }
            return false;
        }

        private static void ParseFilePath(string path, out string bucketName, out string objectKey)
        {
            bucketName = path.Split('/')[0];
            objectKey = path[(path.IndexOf('/') + 1)..];
        }

        private static Dictionary<string, string> GetMetaData(MetadataCollection metadataCollection)
        {
            var metaData = new Dictionary<string, string>();
            foreach (var key in metadataCollection.Keys)
            {
                metaData.Add(key, metadataCollection[key]);
            }
            return metaData;
        }

        // Look for ListObjectsV2 Method : https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/csharp_s3_code_examples.html
        private async Task<List<UploadedFile>> GetObjectListAsync(string bucketName, string? prefix, bool withData = false, CancellationToken cancellationToken = default)
        {
            var files = new List<UploadedFile>();
            ListObjectsV2Response response;

            var listObjectsV2Request = !string.IsNullOrEmpty(prefix) ? new ListObjectsV2Request { BucketName = bucketName, Prefix = prefix }
                                        : new ListObjectsV2Request { BucketName = bucketName };

            do
            {
                response = await _amazonS3Client.ListObjectsV2Async(listObjectsV2Request, cancellationToken);

                foreach (S3Object s3Object in response.S3Objects)
                {
                    (byte[]? data, Dictionary<string, string>? metaData) = await GetObjectAsync(s3Object.BucketName, s3Object.Key, cancellationToken);
                    files.Add(new UploadedFile
                    {
                        Name = s3Object.Key,
                        Path = GetFilePath(s3Object.BucketName, s3Object.Key),
                        MetaData = metaData,
                        Data = withData ? data : default
                    });
                }

                listObjectsV2Request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated);

            return files;
        }

        private async Task<(byte[]? data, Dictionary<string, string>? metaData)> GetObjectAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
        {
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey
            };
            var response = await _amazonS3Client.GetObjectAsync(request, cancellationToken);
            if (response.HttpStatusCode == HttpStatusCode.OK)
            {
                return (ConvertStreamToByteArray(response.ResponseStream), GetMetaData(response.Metadata));
            }
            return (default, default);
        }

        public static byte[] ConvertStreamToByteArray(Stream inputStream)
        {
            using var memoryStream = new MemoryStream();
            inputStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
        #endregion

        #region IDisposable Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // dispose resources
        }

        #endregion
    }
}
