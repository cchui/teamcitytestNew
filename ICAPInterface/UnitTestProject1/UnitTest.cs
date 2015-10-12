using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using Ionic.Zip;


namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest
    {
        string ServerIP = "10.200.23.84";
        int ServerPort = 1344;

        [TestMethod]
        public void TestScanByFileName()
        {
            ICAPInterfaceLib.ICAP intf = new ICAPInterfaceLib.ICAP(ServerIP, ServerPort);

            var r1 = intf.ScanFile(@"TestData\ILIDocumentInfoUpdate.sql");

            var r2 = intf.ScanFile(@"TestData\icap_whitepaper_v1-01.pdf");

            Assert.IsFalse(r1.Success);
            Assert.IsTrue(r1.Message.Equals("McAfee Web Gateway has blocked the file, because the detected media type (see below) is not allowed."));
            Assert.IsTrue(r2.Success);
        }

        [TestMethod]
        public void TestScanByByteArray()
        {
            ICAPInterfaceLib.ICAP intf = new ICAPInterfaceLib.ICAP(ServerIP, ServerPort);

            string f1name = @"TestData\ILIDocumentInfoUpdate.sql";
            byte[] f1byte = System.IO.File.ReadAllBytes(f1name);
            var r1 = intf.ScanFile(f1byte, f1name);

            string f2name = @"TestData\icap_whitepaper_v1-01.pdf";
            byte[] f2byte = System.IO.File.ReadAllBytes(f2name);
            var r2 = intf.ScanFile(f2byte, f2name);

            Assert.IsFalse(r1.Success);
            Assert.IsTrue(r1.Message.Equals("McAfee Web Gateway has blocked the file, because the detected media type (see below) is not allowed."));
            Assert.IsTrue(r2.Success);
        }

        [TestMethod]
        public void TestFileNameChangeContentStillDetected()
        {
            ICAPInterfaceLib.ICAP intf = new ICAPInterfaceLib.ICAP(ServerIP, ServerPort);

            string f1name = @"TestData\ILIDocumentInfoUpdate.sql";
            byte[] f1byte = System.IO.File.ReadAllBytes(f1name);
            var r1 = intf.ScanFile(f1byte, "test.pdf");

            string f2name = @"TestData\icap_whitepaper_v1-01.pdf";
            byte[] f2byte = System.IO.File.ReadAllBytes(f2name);
            var r2 = intf.ScanFile(f2byte, "test1.sql");

            Assert.IsFalse(r1.Success);
            Assert.IsTrue(r1.Message.Equals("McAfee Web Gateway has blocked the file, because the detected media type (see below) is not allowed."));
            Assert.IsTrue(r2.Success);
        }

        [TestMethod]
        public void TestVirusDetected()
        {
            ICAPInterfaceLib.ICAP intf = new ICAPInterfaceLib.ICAP(ServerIP, ServerPort);

            string zipPath = @"TestData\eicarAES-256.zip";  //Password Protected PDF EICAR test file obtained from https://isc.sans.edu/forums/diary/Test+File+PDF+With+Embedded+DOC+Dropping+EICAR/20085/
            using (ZipFile zip = ZipFile.Read(zipPath))
            {
                foreach (ZipEntry e in zip)
                {
                    using (MemoryStream br = new MemoryStream())
                    {
                        e.Password = "EICAR";
                        e.Extract(br);
                        var r2 = intf.ScanFile(br.ToArray(), e.FileName);
                        Assert.IsFalse(r2.Success);
                        Assert.IsTrue(r2.Message.Equals("The transferred file contained a virus and was therefore blocked."));
                    }
                }
            }
        }

        [TestMethod]
        public void TestLargePDF()
        {
            ICAPInterfaceLib.ICAP intf = new ICAPInterfaceLib.ICAP(ServerIP, ServerPort);

            string f1name = @"TestData\10MBTest.pdf";
            byte[] f1byte = System.IO.File.ReadAllBytes(f1name);
            var r1 = intf.ScanFile(f1byte, @"TestData\10MBTest.pdf");

            Assert.IsFalse(r1.Success);
            Assert.IsTrue(r1.Message.Equals("This is a default error message. Please make sure to configure an appropriate error template for rule 'Block large File'."));
        }

        [TestMethod]
        public void TestSimpleValidation()
        {
            ICAPInterfaceLib.ICAP intf = new ICAPInterfaceLib.ICAP(ServerIP, ServerPort);
            string testLargeStr = new string('t', 256);

            string succeededMsg = "Validation succeeded";
            ICAPInterfaceLib.AVValidationMessage avrm =  intf.SimpleValidation(34567, "test.pdf");
            Assert.IsTrue(avrm.IsValidated && avrm.Message.Equals(succeededMsg));

            avrm = intf.SimpleValidation(34567, "test.tif");
            Assert.IsTrue(avrm.IsValidated && avrm.Message.Equals(succeededMsg));

            avrm = intf.SimpleValidation(34567, "test.rtf");
            Assert.IsTrue(avrm.IsValidated && avrm.Message.Equals(succeededMsg));

            avrm = intf.SimpleValidation(34567, "test.PDF");
            Assert.IsTrue(avrm.IsValidated && avrm.Message.Equals(succeededMsg));

            avrm = intf.SimpleValidation(34567, "test.TIF");
            Assert.IsTrue(avrm.IsValidated && avrm.Message.Equals(succeededMsg));

            avrm = intf.SimpleValidation(34567, "test.RTF");
            Assert.IsTrue(avrm.IsValidated && avrm.Message.Equals(succeededMsg));

            avrm = intf.SimpleValidation(34567, "test.tiff");
            Assert.IsFalse(avrm.IsValidated);
            Assert.IsTrue(avrm.Message.Equals("Invalid filetype extension .tiff in filename"));

            avrm = intf.SimpleValidation(34567, "test.txt");
            Assert.IsTrue(avrm.Message.Equals("Invalid filetype extension .txt in filename"));

            avrm = intf.SimpleValidation(34567, "te.st.pdf");
            Assert.IsFalse(avrm.IsValidated);

            avrm = intf.SimpleValidation(34567, "te.tif.pdf");
            Assert.IsFalse(avrm.IsValidated);
            Assert.IsTrue(avrm.Message.Equals("Invalid character in filename"));

            avrm = intf.SimpleValidation(34567, "te\tif.pdf");
            Assert.IsFalse(avrm.IsValidated);
            Assert.IsTrue(avrm.Message.Equals("Invalid file name"));

            avrm = intf.SimpleValidation(34567, "te/tif.pdf");
            Assert.IsFalse(avrm.IsValidated);
            Assert.IsTrue(avrm.Message.Equals("Invalid character in filename"));

            avrm = intf.SimpleValidation(34567444, "test.pdf");
            Assert.IsFalse(avrm.IsValidated);
            Assert.IsTrue(avrm.Message.StartsWith("The file size is greater than"));

            avrm = intf.SimpleValidation(34567, testLargeStr + ".pdf");
            Assert.IsFalse(avrm.IsValidated);
            Assert.IsTrue(avrm.Message.StartsWith("The lenght of filename is greater than"));
        }

        [TestMethod]
        public void TestBadFilename()
        {
            ICAPInterfaceLib.ICAP intf = new ICAPInterfaceLib.ICAP(ServerIP, ServerPort);
            string f2name = @"TestData\icap_whitepaper_v1-01.pdf";
            byte[] f2byte = System.IO.File.ReadAllBytes(f2name);
            var r2 = intf.ScanFile(f2byte, "test 1.sql");

            Assert.IsTrue(r2.Success);
        }


      
    }
}
