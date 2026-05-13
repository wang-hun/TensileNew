using NPOI.HSSF.UserModel;  // 用于 .xls 格式
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;  // 用于 .xlsx 格式
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Drawing;
using System.IO;
using TensileNeW.Models;
using LicenseContext = OfficeOpenXml.LicenseContext;

namespace TensileNeW.Tools
{
    /// <summary>
    /// 效率太低，弃用
    /// </summary>
    public class ExcelExporter
    {
        /// <summary>
        /// 将集合导出到 Excel
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="dataList">数据集合</param>
        /// <param name="filePath">保存路径（支持.xls和.xlsx）</param>
        public static void ExportToExcel1 (IList<Loadmodel> dataList, string filePath)
        {
            if (dataList == null || dataList.Count == 0)
                return;

            IWorkbook workbook;
            string extension = Path.GetExtension(filePath).ToLower();

            // 根据扩展名创建对应格式的Workbook
            if (extension == ".xlsx")
                workbook = new XSSFWorkbook();
            else if (extension == ".xls")
                workbook = new HSSFWorkbook();
            else
                throw new NotSupportedException("不支持的文件格式");

            ISheet sheet = workbook.CreateSheet("Sheet1");

            // 生成标题行（使用反射获取属性名）
            var headers = new string[] {"序号", "压力", "位移", "载荷", "时间"};
            IRow headerRow = sheet.CreateRow(0);
            for (int i = 0; i < headers.Length; i++)
            {
                headerRow.CreateCell(i).SetCellValue(headers[i]);
            }

            // 填充数据行
            for (int rowIdx = 0; rowIdx < dataList.Count; rowIdx++)
            {
                IRow dataRow = sheet.CreateRow(rowIdx + 1);
                var item = dataList[rowIdx];
                ICell cell = dataRow.CreateCell(0); 
                cell.SetCellValue(Convert.ToInt32(item.Index));

                cell = dataRow.CreateCell(1);
                cell.SetCellValue(Convert.ToDouble(item.RealPress));
                 
                cell = dataRow.CreateCell(2);
                cell.SetCellValue(Convert.ToDouble(item.RealDistance));
                 
                cell = dataRow.CreateCell(3);
                cell.SetCellValue(Convert.ToDouble(item.RealForce));
                 
                cell = dataRow.CreateCell(4);
                cell.SetCellValue(item.Time);

             
            }

            // 自动调整列宽
            for (int i = 0; i < headers.Length; i++)
            {
                sheet.AutoSizeColumn(i);
            }

            // 保存文件
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                workbook.Write(fs);
            }
        }


        /// <summary>
        /// 将集合导出到 Excel
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="dataList">数据集合</param>
        /// <param name="filePath">保存路径（支持.xls和.xlsx）</param>
        public static void ExportToExcel<T>(IList<T> dataList, string filePath)
        {
            if (dataList == null || dataList.Count == 0)
               return ;

            IWorkbook workbook;
            string extension = Path.GetExtension(filePath).ToLower();

            // 根据扩展名创建对应格式的Workbook
            if (extension == ".xlsx")
                workbook = new XSSFWorkbook();
            else if (extension == ".xls")
                workbook = new HSSFWorkbook();
            else
                throw new NotSupportedException("不支持的文件格式");

            ISheet sheet = workbook.CreateSheet("Sheet1");

            // 生成标题行（使用反射获取属性名）
            var properties = typeof(T).GetProperties();
            IRow headerRow = sheet.CreateRow(0);
            for (int i = 0; i < properties.Length; i++)
            {
                headerRow.CreateCell(i).SetCellValue(properties[i].Name);
            }

            // 填充数据行
            for (int rowIdx = 0; rowIdx < dataList.Count; rowIdx++)
            {
                IRow dataRow = sheet.CreateRow(rowIdx + 1);
                T item = dataList[rowIdx];

                for (int colIdx = 0; colIdx < properties.Length; colIdx++)
                {
                    object value = properties[colIdx].GetValue(item, null);
                    ICell cell = dataRow.CreateCell(colIdx);

                    // 根据数据类型设置单元格格式
                    if (value == null)
                    {
                        cell.SetCellValue(string.Empty);
                    }
                    else if (value is DateTime)
                    {
                        cell.SetCellValue((DateTime)value);
                        // 设置日期格式
                        ICellStyle dateStyle = workbook.CreateCellStyle();
                        dateStyle.DataFormat = workbook.CreateDataFormat().GetFormat("yyyy-mm-dd");
                        cell.CellStyle = dateStyle;
                    }
                    else if (IsNumeric(value))
                    {
                        cell.SetCellValue(Convert.ToDouble(value));
                    }
                    else
                    {
                        cell.SetCellValue(value.ToString());
                    }
                }

                
            }

            // 自动调整列宽
            for (int i = 0; i < properties.Length; i++)
            {
                sheet.AutoSizeColumn(i);
            }

            // 保存文件
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                workbook.Write(fs);
            }
        }

        private static bool IsNumeric(object value)
        {
            return value is int || value is double || value is decimal
                   || value is float || value is long || value is short;
        }
    }

    
    public class ExcelExporter_EPPlus:IDisposable
    {
        private ExcelPackage _package;
        private ExcelWorksheet _sheet;
        private int _currentRow = 1;

        /// <summary>
        /// 初始化 Excel 导出器
        /// </summary>
        public ExcelExporter_EPPlus()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            _package = new ExcelPackage();
        }

        /// <summary>
        /// 创建新工作表
        /// </summary>
        public ExcelExporter_EPPlus CreateSheet(string sheetName = "Sheet1")
        {
            _sheet = _package.Workbook.Worksheets.Add(sheetName);
            //在写入大量数据前关闭自动计算
            _package.Workbook.CalcMode = ExcelCalcMode.Manual;
            return this;
        }

        /// <summary>
        /// 设置标题行（支持合并单元格）
        /// </summary>
        public ExcelExporter_EPPlus SetHeader(string[] headers,
            Color? bgColor = null,
            bool mergeCells = false,
            int mergeStartCol = 1)
        {
            // 创建标题行
            var headerRow = _sheet.Cells[_currentRow, 1, _currentRow, headers.Length];

            // 合并单元格
            if (mergeCells)
            {
                _sheet.Cells[_currentRow, mergeStartCol, _currentRow, headers.Length + mergeStartCol - 1].Merge = true;
            }

            // 填充数据
            for (int i = 0; i < headers.Length; i++)
            {
                _sheet.Cells[_currentRow, i + 1].Value = headers[i];
            }

            // 设置样式
            var style = _sheet.Rows[_currentRow].Style;
            style.Font.Bold = true;
            if (bgColor.HasValue)
            {
                style.Fill.PatternType = ExcelFillStyle.Solid;
                style.Fill.BackgroundColor.SetColor(bgColor.Value);
            }

            _currentRow++;
            return this;
        }

        /// <summary>
        /// 写入 DataTable 数据
        /// </summary>
        public ExcelExporter_EPPlus AddData(DataTable data,  bool autoFitColumns = true, Action<ExcelRange>? styleAction = null)
        {
            if (data == null || data.Rows.Count == 0) return this;

            // 写入列名（可选）
            for (int col = 0; col < data.Columns.Count; col++)
            {
                _sheet.Cells[_currentRow, col + 1].Value = data.Columns[col].ColumnName;
            }
            _currentRow++;

            // 写入数据
            for (int row = 0; row < data.Rows.Count; row++)
            {
                for (int col = 0; col < data.Columns.Count; col++)
                {
                    var cell = _sheet.Cells[_currentRow + row, col + 1];
                    cell.Value = data.Rows[row][col];

                    // 自动识别数据类型
                    if (data.Columns[col].DataType == typeof(DateTime))
                    {
                        cell.Style.Numberformat.Format = "yyyy-mm-dd";
                    }
                }
            }

            // 应用样式
            if (styleAction != null)
            {
                var dataRange = _sheet.Cells[_currentRow, 1, _currentRow + data.Rows.Count - 1, data.Columns.Count];
                styleAction(dataRange);
            }

            _currentRow += data.Rows.Count;

            // 自动列宽
            if (autoFitColumns)
            {
                _sheet.Cells[_sheet.Dimension.Address].AutoFitColumns();
            }

            return this;
        }

        /// <summary>
        /// 写入泛型集合数据
        /// </summary>
        public ExcelExporter_EPPlus AddData<T>(IEnumerable<T> data,Func<T, object[]> dataSelector,bool autoFitColumns = true)
        {
            if (data == null) return this;

            int col = 1;
            foreach (var item in data)
            {
                var values = dataSelector(item);
                for (int i = 0; i < values.Length; i++)
                {
                    _sheet.Cells[_currentRow, col + i].Value = values[i];
                }
                _currentRow++;
            }

            if (autoFitColumns)
            {
                _sheet.Cells[_sheet.Dimension.Address].AutoFitColumns();
            }

            return this;
        }

        /// <summary>
        /// 保存到文件
        /// </summary>
        public void SaveToFile(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                _package.SaveAs(stream);
            }
        }

        /// <summary>
        /// 获取内存流（用于 Web 导出）
        /// </summary>
        public MemoryStream GetStream()
        {
            var stream = new MemoryStream();
            _package.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public  void Dispose()
        {
            _package?.Dispose();
        }
    }

}

