namespace Dotto.Application.Abstractions.Upload;

// TODO: this should be moved out into FileUpload.Contracts
public interface IUploadService
{
    /// <summary>Largest file the upload backend accepts, in bytes.</summary>
    public long MaxFileSizeBytes { get; }

    public Task<Uri> UploadFile(Stream stream, long fileSize, string? filename, string? contentType, CancellationToken token);
}