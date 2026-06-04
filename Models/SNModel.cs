using OfficeOpenXml.FormulaParsing.Excel.Functions.Engineering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Models
{
    public static class SNModel
    {
        public static int SN { get; set; } = 1;

        public static string GetSn()
        {
            return SN.ToString("D5");
        }

        public static bool HasSnFile()
        {
            return File.Exists(GetSnFilePath());
        }

        public static void WriteSN()
        {
            SN += 1;
            WriteSN(SN); 

        }
        private static void WriteSN(int i)
        {

            string filePath = GetSnFilePath();
            // 删除文件（如果存在）
            if (!File.Exists(filePath))
            {
                 File.Create(filePath).Close();
            }

            // 一次性写入文件
            File.WriteAllText(filePath, i.ToString());

        }

        public static void LoadSN()
        {
            string filePath = GetSnFilePath();

            if (!File.Exists(filePath))
            {
                WriteSN(1);
            }

            string lines = File.ReadAllText(filePath);

            if (int.TryParse(lines, out int intValue))
            {
                SN = intValue;

            }

        }

        private static string GetSnFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SN.txt");
        }

    }
}

