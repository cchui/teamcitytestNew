using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;


namespace ICAPInterfaceLib
{

    public class ICAP : IDisposable
    {
        private const String ICAPTERMINATOR = "\r\n\r\n";
        private const String HTTPTERMINATOR = "0\r\n\r\n";

        private String serverIP;
        private int port;

        private Socket sender;
        private String service;
        private String version;
        private int sendPartition;
        private int stdRecieveLength;
        private int sendTimeout;
        private int receiveTimeout;
        private bool keepAlive;

        private int fileNameLength;
        private int maxFileSize;
        private string allowedFileTypeExtension;
        private string excludedStringInFileName;
       

        public ICAP(string serverIP, int port)
        {
            this.serverIP = serverIP;
            this.port = port;

            //UpdateSettingWithLocalConfig();
            UpdateSettingWithConfig();

            SetupSocket(serverIP, port);   
        }

        public AVReturnMessage ScanFile(String filepath)
        {
            byte[] f1byte = System.IO.File.ReadAllBytes(filepath);
            return ScanFile(f1byte, filepath);
        }


        public AVReturnMessage ScanFile(byte[] filedata, string filename)
        {
            if (filedata.Length == 0)
            {
                throw new ICAPException("Invalid file size");
            }
            int stdSendLength = (int)Math.Ceiling((double)filedata.Length / (double)sendPartition);
            SendClientRequest(filedata.Length, System.Web.HttpUtility.UrlEncode(filename));

            int skip = 0, end = 0;
            do {
                end += stdSendLength;
                if (end > filedata.Length)
                {
                    end = filedata.Length;
                }
                int outlength = end - skip; 
                sender.Send(Encoding.ASCII.GetBytes(outlength.ToString("X") + "\r\n"));
                sender.Send(filedata.Skip(skip).Take(outlength).ToArray());
                sender.Send(Encoding.ASCII.GetBytes("\r\n"));
                skip = end;
            } while (end < filedata.Length);

            sender.Send(Encoding.ASCII.GetBytes("0\r\n\r\n"));
            return GetServerResponse();
        }


        public AVValidationMessage SimpleValidation(int fileSize, string filename)
        {
            if (fileSize > maxFileSize || fileSize <= 0)
            {
                return new AVValidationMessage() { IsValidated = false, Message = "The file size is greater than " + maxFileSize + " bytes"};
            }

            if (filename.Length > fileNameLength)
            {
                return new AVValidationMessage() { IsValidated = false, Message = "The lenght of filename is greater than " + fileNameLength};
            }

            try
            {
                var extensionArray = allowedFileTypeExtension.Split(',').Select(i => i.Trim()).ToArray();
                var processExtension = Path.GetExtension(filename);
                if (extensionArray.All(i => !processExtension.EndsWith("." + i, true, null)))
                {
                    return new AVValidationMessage() { IsValidated = false, Message = "Invalid filetype extension " + processExtension + " in filename" };
                }
            }
            catch (Exception ex)
            {
                 return new AVValidationMessage() { IsValidated = false, Message = "Invalid file name" };
            }

            try
            {
                var excludedStrArray = excludedStringInFileName.Split(',').Select(i => i.Trim()).ToArray();
                var processFilename = filename.Substring(0, filename.LastIndexOf("."));
                if (excludedStrArray.Any(i => processFilename.Contains(i)))
                {
                    return new AVValidationMessage() { IsValidated = false, Message = "Invalid character in filename"};
                }
            }
            catch (Exception ex)
            {
                return new AVValidationMessage() { IsValidated = false, Message = "Invalid character in filename" };
            }

            return new AVValidationMessage() { IsValidated = true, Message = "Validation succeeded" };;
        }

        private void UpdateSettingWithConfig()
        {

            try
            {
                service = ConfigurationManager.AppSettings["Service"];
                version = ConfigurationManager.AppSettings["ICAPVersion"];
                stdRecieveLength = Convert.ToInt32(ConfigurationManager.AppSettings["BufferReceiveLength"]);
                sendPartition = Convert.ToInt32(ConfigurationManager.AppSettings["SendPartition"]);
                sendTimeout = Convert.ToInt32(ConfigurationManager.AppSettings["SendTimeoutInMiliseconds"]);
                receiveTimeout = Convert.ToInt32(ConfigurationManager.AppSettings["ReceiveTimeoutInMiliseconds"]);
                keepAlive = ConfigurationManager.AppSettings["SocketKeepAlive"].ToUpper().Equals("TRUE") ? true : false;

                fileNameLength = Convert.ToInt32(ConfigurationManager.AppSettings["FileNameLength"]);
                maxFileSize = Convert.ToInt32(ConfigurationManager.AppSettings["MaximumFileSizeInBytes"]);
                allowedFileTypeExtension = ConfigurationManager.AppSettings["AllowedFileTypeExtension"];
                excludedStringInFileName = ConfigurationManager.AppSettings["ExcludedStringInFileNameNotIncludingExtension"];
            }
            catch (Exception)
            {
                throw new ICAPException("App.config or Web.config is invalid");
            }

        }

        private void SetupSocket(string serverIPLocal, int portLocal)
        {
            //Initialize connection
            IPAddress ipAddress = IPAddress.Parse(serverIPLocal);
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, portLocal);

            // Create a TCP/IP  socket.
            sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            if (receiveTimeout > 0) sender.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, receiveTimeout);
            if (sendTimeout > 0) sender.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, receiveTimeout);
            if (!keepAlive) sender.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, keepAlive);

            sender.Connect(remoteEP);
        }

        private AVReturnMessage GetServerResponse()
        {
            Dictionary<String, String> responseMap = new Dictionary<string, string>();
            String response = GetHeader(ICAPTERMINATOR);
            responseMap = ParseHeader(response);
            String tempString;
            int status;

            responseMap.TryGetValue("StatusCode", out tempString);

            if (tempString != null)
            {
                try
                {
                    status = Convert.ToInt16(tempString);
                } catch (Exception){
                    throw new ICAPException("Unrecognized or no status code in response header.");
                }

                if (status == 204) 
                {
                    return new AVReturnMessage() { Success = true, ICAPStatusCode = status, Message = "File Scanned Successfully" }; 
                } //Unmodified

                if (status == 200) //OK - The ICAP status is ok, but the encapsulated HTTP status will likely be different
                {
                    response = GetHeader(HTTPTERMINATOR);
                    var retMsg = GetHttpResponseMessage(response);
                    if (retMsg != null)
                    {
                        return new AVReturnMessage() { Success = false, ICAPStatusCode = status, Message = retMsg }; 
                    }
                }    
            }
            throw new ICAPException("Unrecognized or no status code in response header.");
        }

        private void SendClientRequest(int fileSize, string filename)
        {
            String resBody = "HTTP/1.1 200 OK\r\n" +
                                "Transfer-Encoding: chunked\r\n" +
                                "Content-Length: " + fileSize + "\r\n\r\n";

            String httpRequest = "GET http://" + serverIP + "/" + DateTime.Now.ToString("yyyyMMddHHmm") + "/" + Path.GetFileName(filename) + " HTTP/1.1\r\n"
                                    + "Host: " + serverIP + "\r\n\r\n";

            byte[] requestBuffer = Encoding.ASCII.GetBytes(
                "RESPMOD icap://" + serverIP + ":" + port.ToString() + "/" + service + " " + version + "\r\n"
                + "Allow: 204\r\n"
                + "Connection: close\r\n"
                + "Host: " + serverIP + "\r\n"
                + "Encapsulated: req-hdr=0, res-hdr=" + httpRequest.Length + " res-body=" + (resBody.Length + httpRequest.Length) + "\r\n"
                + "\r\n"
                + httpRequest
                + resBody);

            sender.Send(requestBuffer);
        }

        private String GetHttpResponseMessage(string httpResponse)
        {
            try
            {
                int firstIndex = httpResponse.IndexOf("contentData");
                int secondIndex = httpResponse.IndexOf("</td>", firstIndex);
                int offset = 14;
                string content = httpResponse.Substring(firstIndex+offset, secondIndex - firstIndex - offset);

                return content.Trim();
            } catch (Exception ex)
            {
                //throw new ICAPException("Invalid McAfee content data due to change of template format");;
                return "Invalid McAfee content data due to change of template format";
            }
        }

        private String GetHeader(String terminator)
        {
            byte[] endofheader = System.Text.Encoding.UTF8.GetBytes(terminator);
            byte[] buffer = new byte[stdRecieveLength];

            int n;
            int offset = 0;
            //stdRecieveLength-offset is replaced by '1' to not receive the next (HTTP) header.
            while ((offset < stdRecieveLength) && ((n = sender.Receive(buffer, offset, 1, SocketFlags.None)) != 0)) // first part is to secure against DOS
            {
                offset += n;
                if (offset > endofheader.Length + 13) // 13 is the smallest possible message (ICAP/1.0 xxx\r\n) or (HTTP/1.0 xxx\r\n)
                {
                    byte[] lastBytes = new byte[endofheader.Length];
                    Array.Copy(buffer, offset - endofheader.Length, lastBytes, 0, endofheader.Length);
                    if (endofheader.SequenceEqual(lastBytes))
                    {
                        return Encoding.ASCII.GetString(buffer, 0, offset);
                    }
                }
            }
            throw new ICAPException("Error in getHeader() method -  try increasing the size of stdRecieveLength");
        }

        private Dictionary<String, String> ParseHeader(String response)
        {
            Dictionary<String, String> headers = new Dictionary<String, String>();

            /****SAMPLE:****
             * ICAP/1.0 204 Unmodified
             * Server: C-ICAP/0.1.6
             * Connection: keep-alive
             * ISTag: CI0001-000-0978-6918203
             */
            // The status code is located between the first 2 whitespaces.
            // Read status code
            int x = response.IndexOf(" ", 0);
            int y = response.IndexOf(" ", x + 1);
            String statusCode = response.Substring(x + 1, y - x - 1);
            headers.Add("StatusCode", statusCode);

            // Each line in the sample is ended with "\r\n". 
            // When (i+2==response.length()) The end of the header have been reached.
            // The +=2 is added to skip the "\r\n".
            // Read headers
            int i = response.IndexOf("\r\n", y);
            i += 2;
            while (i + 2 != response.Length && response.Substring(i).Contains(':'))
            {
                int n = response.IndexOf(":", i);
                String key = response.Substring(i, n - i);

                n += 2;
                i = response.IndexOf("\r\n", n);
                String value = response.Substring(n, i - n);

                headers.Add(key, value);
                i += 2;
            }
            return headers;
        }

        public void Dispose()
        {
            sender.Shutdown(SocketShutdown.Both);
            sender.Close();
        }
    }
}
