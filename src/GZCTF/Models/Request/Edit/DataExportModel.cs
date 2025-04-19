namespace GZCTF.Models.Request.Edit;

/// <summary>
/// Generic Data Import/Export Model
/// </summary>
public class DataExportModel
{
    /// <summary>
    /// The serialized data of the thing to export/import
    /// </summary>
    public string Data { get; set; } = string.Empty;
}
