// Copyright 2025 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Luban.CustomBehaviour;
using Luban.Datas;
using Luban.Defs;
using Luban.Types;
using Luban.Utils;
using System.Globalization;

namespace Luban.DataLoader;

public class DataLoaderManager
{
    private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

    public static DataLoaderManager Ins { get; } = new();

    public void Init()
    {

    }

    public void LoadDatas(GenerationContext ctx)
    {
        var tasks = ctx.Tables.Select(t => Task.Run(() => LoadTable(ctx, t))).ToArray();
        Task.WaitAll(tasks);
    }

    private void LoadTable(GenerationContext ctx, DefTable table)
    {
        if (table.HasTag("dirTable"))
        {
            ctx.AddDataTable(table, LoadDirTable(table), null);
            return;
        }

        string inputDataDir = GenerationContext.GetInputDataPath();
        var tasks = new List<Task<List<Record>>>();
        foreach (var inputFile in table.InputFiles)
        {
            s_logger.Trace("load table:{} file:{}", table.FullName, inputFile);
            var (actualFile, subAssetName) = FileUtil.SplitFileAndSheetName(FileUtil.Standardize(inputFile));
            var options = new Dictionary<string, string>();
            foreach (var atomFile in FileUtil.GetFileOrDirectory(inputDataDir, Path.Combine(inputDataDir, actualFile)))
            {
                s_logger.Trace("load table:{} atomfile:{}", table.FullName, atomFile);
                tasks.Add(Task.Run(() => LoadTableFile(table, atomFile, subAssetName, options)));
            }
        }

        var records = new List<Record>();
        foreach (var task in tasks)
        {
            records.AddRange(task.Result);
        }
        ctx.AddDataTable(table, records, null);
    }

    private List<Record> LoadDirTable(DefTable table)
    {
        if (!table.IsMapTable)
        {
            throw new Exception($"dirTable:'{table.FullName}' only supports mode=map");
        }

        string keyFieldName = GetRequiredTableTag(table, "fileKeyField");
        string valueFieldName = GetRequiredTableTag(table, "fileValueField");
        string keyType = NormalizeDirTableKeyType(GetRequiredTableTag(table, "fileKeyType"));

        if (!table.ValueTType.DefBean.TryGetField(keyFieldName, out var keyField, out var keyFieldIndex))
        {
            throw new Exception($"dirTable:'{table.FullName}' fileKeyField:'{keyFieldName}' not found in value type:'{table.ValueTType.DefBean.FullName}'");
        }
        if (keyField != table.IndexField)
        {
            throw new Exception($"dirTable:'{table.FullName}' fileKeyField:'{keyFieldName}' must be the table index field:'{table.Index}'");
        }
        ValidateDirTableKeyType(table, keyType, keyField.CType);

        if (!table.ValueTType.DefBean.TryGetField(valueFieldName, out var valueField, out var valueFieldIndex))
        {
            throw new Exception($"dirTable:'{table.FullName}' fileValueField:'{valueFieldName}' not found in value type:'{table.ValueTType.DefBean.FullName}'");
        }
        TType valueFieldType = valueField.CType;
        if (valueFieldType is not TList and not TArray)
        {
            throw new Exception($"dirTable:'{table.FullName}' fileValueField:'{valueFieldName}' must be list or array type");
        }
        if (valueFieldType.ElementType is not TBean elementBeanType)
        {
            throw new Exception($"dirTable:'{table.FullName}' fileValueField:'{valueFieldName}' element type must be bean");
        }

        string inputDataDir = GenerationContext.GetInputDataPath();
        var tasks = new List<Task<Record>>();
        foreach (var inputFile in table.InputFiles)
        {
            var (actualFile, subAssetName) = FileUtil.SplitFileAndSheetName(FileUtil.Standardize(inputFile));
            foreach (var atomFile in FileUtil.GetFileOrDirectory(inputDataDir, Path.Combine(inputDataDir, actualFile)))
            {
                tasks.Add(Task.Run(() => LoadDirTableFile(table, atomFile, subAssetName, keyType, keyFieldIndex, valueFieldIndex, valueFieldType, elementBeanType)));
            }
        }

        var records = new List<Record>();
        foreach (var task in tasks)
        {
            records.Add(task.Result);
        }
        return records;
    }

    private Record LoadDirTableFile(DefTable table, string atomFile, string subAssetName, string keyType, int keyFieldIndex,
        int valueFieldIndex, TType valueFieldType, TBean elementBeanType)
    {
        var records = LoadTableFile(elementBeanType, atomFile, subAssetName, new Dictionary<string, string>());
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(atomFile);
        DType key = ParseDirTableKey(table, keyType, table.IndexField.CType, fileNameWithoutExt);

        var fields = new DType[table.ValueTType.DefBean.HierarchyFields.Count];
        fields[keyFieldIndex] = key;
        fields[valueFieldIndex] = CreateDirTableValueCollection(valueFieldType, records);

        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i] == null)
            {
                throw new Exception($"dirTable:'{table.FullName}' value type:'{table.ValueTType.DefBean.FullName}' field:'{table.ValueTType.DefBean.HierarchyFields[i].Name}' is not filled by dirTable loader");
            }
        }

        return new Record(new DBean(table.ValueTType, table.ValueTType.DefBean, fields.ToList()), atomFile, null);
    }

    private static DType CreateDirTableValueCollection(TType valueFieldType, List<Record> records)
    {
        var datas = records.Select(r => (DType)r.Data).ToList();
        return valueFieldType switch
        {
            TList listType => new DList(listType, datas),
            TArray arrayType => new DArray(arrayType, datas),
            _ => throw new NotSupportedException(),
        };
    }

    private static string GetRequiredTableTag(DefTable table, string tagName)
    {
        string value = table.GetTag(tagName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new Exception($"dirTable:'{table.FullName}' requires tag:'{tagName}'");
        }
        return value.Trim();
    }

    private static string NormalizeDirTableKeyType(string keyType)
    {
        return keyType.Trim().ToLowerInvariant() switch
        {
            "uint8" or "byte" => "byte",
            "int16" or "short" => "short",
            "int32" or "int" => "int",
            "int64" or "long" or "bigint" => "long",
            "string" => "string",
            _ => throw new Exception($"dirTable fileKeyType:'{keyType}' is not supported"),
        };
    }

    private static void ValidateDirTableKeyType(DefTable table, string keyType, TType actualType)
    {
        bool matched = keyType switch
        {
            "byte" => actualType is TByte,
            "short" => actualType is TShort,
            "int" => actualType is TInt,
            "long" => actualType is TLong,
            "string" => actualType is TString,
            _ => false,
        };
        if (!matched)
        {
            throw new Exception($"dirTable:'{table.FullName}' fileKeyType:'{keyType}' does not match fileKeyField:'{table.IndexField.Name}' type:'{actualType.TypeName}'");
        }
    }

    private static DType ParseDirTableKey(DefTable table, string keyType, TType actualType, string fileNameWithoutExt)
    {
        try
        {
            return keyType switch
            {
                "byte" => DByte.ValueOf(byte.Parse(fileNameWithoutExt, CultureInfo.InvariantCulture)),
                "short" => DShort.ValueOf(short.Parse(fileNameWithoutExt, CultureInfo.InvariantCulture)),
                "int" => DInt.ValueOf(int.Parse(fileNameWithoutExt, CultureInfo.InvariantCulture)),
                "long" => DLong.ValueOf(long.Parse(fileNameWithoutExt, CultureInfo.InvariantCulture)),
                "string" => DString.ValueOf(actualType, fileNameWithoutExt),
                _ => throw new NotSupportedException(),
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new Exception($"dirTable:'{table.FullName}' file name:'{fileNameWithoutExt}' can not be parsed as fileKeyType:'{keyType}'", e);
        }
    }

    public List<Record> LoadTableFile(DefTable table, string file, string subAssetName, Dictionary<string, string> options)
    {
        try
        {
            s_logger.Trace("load table:{} file:{}", table.FullName, file);
            if (!File.Exists(file) && !Directory.Exists(file))
            {
                throw new Exception($"'{table.FullName}'的input文件或目录不存在: {file} ");
            }
            string loaderName = options.TryGetValue("loader", out var name) ? name : FileUtil.GetExtensionWithoutDot(file);
            var loader = CreateDataLoader(loaderName);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            loader.Load(file, subAssetName, stream);
            if (IsMultiRecordFile(file, subAssetName))
            {
                return loader.ReadMulti(table.ValueTType);
            }
            return new List<Record> { loader.ReadOne(table.ValueTType) };
        }
        catch (DataCreateException e)
        {
            if (string.IsNullOrEmpty(e.OriginDataLocation))
            {
                e.OriginDataLocation = file;
            }
            throw;
        }
        catch (Exception e)
        {
            throw new Exception($"LoadTableFile fail. {file}", e);
        }
    }

    public List<Record> LoadTableFile(TBean valueType, string file, string subAssetName, Dictionary<string, string> options)
    {
        try
        {
            string loaderName = options.TryGetValue("loader", out var name) ? name : FileUtil.GetExtensionWithoutDot(file);
            var loader = CreateDataLoader(loaderName);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            loader.Load(file, subAssetName, stream);
            if (IsMultiRecordFile(file, subAssetName))
            {
                return loader.ReadMulti(valueType);
            }
            return new List<Record> { loader.ReadOne(valueType) };
        }
        catch (DataCreateException e)
        {
            if (string.IsNullOrEmpty(e.OriginDataLocation))
            {
                e.OriginDataLocation = file;
            }
            throw;
        }
        catch (Exception e)
        {
            throw new Exception($"LoadTableFile fail. {file}", e);
        }
    }

    private static bool IsMultiRecordField(string sheet)
    {
        return !string.IsNullOrEmpty(sheet) && sheet.StartsWith("*");
    }

    private static bool IsMultiRecordFile(string file, string sheetOrFieldName)
    {
        return FileUtil.IsExcelFile(file) || IsMultiRecordField(sheetOrFieldName);
    }

    public IDataLoader CreateDataLoader(string loaderName)
    {
        return CustomBehaviourManager.Ins.CreateBehaviour<IDataLoader, DataLoaderAttribute>(loaderName);
    }
}
