using System;
using System.IO;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using MetricsExtractor.Core.Integration.Abstract;

namespace MetricsExtractor.Core.Integration.Concrete
{
    public class AmazonS3Integration : IAmazonS3Integration
    {
        private IAmazonS3 amazonS3Client;

        public AmazonS3Integration(string accessKey, string secretKey)
        {
            amazonS3Client = new AmazonS3Client(accessKey, secretKey, RegionEndpoint.USEast1);
        }

        public void SendDocument(string filePath, string bucket, string destinationPath, string fileNamOnDestinationWithExtension = "index.html")
        {
            try
            {
                var transferUtility = new TransferUtility(amazonS3Client);
                if (!transferUtility.S3Client.DoesS3BucketExist(bucket))
                    transferUtility.S3Client.PutBucket(new PutBucketRequest { BucketName = bucket });

                var request = new TransferUtilityUploadRequest
                {
                    BucketName = bucket,
                    Key = string.Format("{0}/{1}", destinationPath, fileNamOnDestinationWithExtension),
                    FilePath = filePath
                };
                request.UploadProgressEvent += uploadFileProgressCallback;

                transferUtility.Upload(request);
                transferUtility.Dispose();
            }
            catch (Exception ex)
            {
                throw new Exception("Error send file to S3. " + ex.Message);
            }
        }

        public string SignUrl(string path, string docName, string bucket, int segundosParaExpiracao = 2592000)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key = string.Format("{0}/{1}", path, docName),
                Expires = DateTime.Now.AddSeconds(segundosParaExpiracao),
                Protocol = Protocol.HTTPS
            };

            var urlAssinada = amazonS3Client.GetPreSignedURL(request);
            return urlAssinada;
        }

        private void uploadFileProgressCallback(object sender, UploadProgressArgs e)
        {
            Console.WriteLine("Total bytes: {0} - Total sent: {1}", e.TotalBytes, e.TransferredBytes);
        }
    }
}
