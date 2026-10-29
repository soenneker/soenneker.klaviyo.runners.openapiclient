using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Soenneker.Extensions.String;
using Soenneker.Git.Util.Abstract;
using Soenneker.Klaviyo.Runners.OpenApiClient.Utils.Abstract;
using Soenneker.Utils.Dotnet.Abstract;
using Soenneker.Utils.Environment;

using System;
using System.IO;

using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;
using Soenneker.Kiota.Util.Abstract;

using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.Yaml.Abstract;

namespace Soenneker.Klaviyo.Runners.OpenApiClient.Utils;
public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IConfiguration _configuration;
    private readonly IGitUtil _gitUtil;
    private readonly IDotnetUtil _dotnetUtil;
    private readonly IKiotaUtil _kiotaUtil;

    private readonly IFileDownloadUtil _fileDownloadUtil;
    private readonly IFileUtil _fileUtil;

    private readonly IYamlUtil _yamlUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IConfiguration configuration, IGitUtil gitUtil, IDotnetUtil dotnetUtil,
        IFileDownloadUtil fileDownloadUtil, IFileUtil fileUtil, IKiotaUtil kiotaUtil,
        IYamlUtil yamlUtil)
    {
        _logger = logger;
        _configuration = configuration;
        _gitUtil = gitUtil;
        _dotnetUtil = dotnetUtil;
        _kiotaUtil = kiotaUtil;

        _fileDownloadUtil = fileDownloadUtil;
        _fileUtil = fileUtil;

        _yamlUtil = yamlUtil;
    }

    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        string? localDirectory = _configuration["Klaviyo:LocalRepositoryPath"];
        string gitDirectory = localDirectory == null ? await _gitUtil.CloneToTempDirectory($"https://github.com/soenneker/{Constants.Library.ToLowerInvariantFast()}", cancellationToken: cancellationToken) : Path.GetFullPath(localDirectory);

        if (!File.Exists(Path.Combine(gitDirectory, "src", Constants.Library, Constants.Library + ".csproj")))
            throw new InvalidOperationException("The target directory is not a Klaviyo client repository.");

        string targetFilePath = Path.Combine(gitDirectory, "openapi.json");

        await _fileUtil.DeleteIfExists(targetFilePath, cancellationToken: cancellationToken);

        string openApiDocumentUrl = _configuration["Klaviyo:ClientGenerationUrl"] ?? "https://raw.githubusercontent.com/klaviyo/openapi/2026-10-15/openapi/stable.json";

        string? filePath = await _fileDownloadUtil.Download(openApiDocumentUrl,
            targetFilePath, fileExtension: ".json", cancellationToken: cancellationToken);

        if (filePath == null)
            throw new InvalidOperationException("Klaviyo OpenAPI document download failed.");

        string rawDocument = await _fileUtil.Read(filePath, cancellationToken: cancellationToken);
        string trimmedDocument = rawDocument.TrimStart();

        if (!trimmedDocument.StartsWith('{') && !trimmedDocument.StartsWith('['))
        {
            string convertedFilePath = Path.Combine(gitDirectory, "openapi.converted.json");
            await _fileUtil.DeleteIfExists(convertedFilePath, cancellationToken: cancellationToken);
            await _yamlUtil.SaveAsJson(filePath, convertedFilePath, cancellationToken: cancellationToken);
            filePath = convertedFilePath;
        }

        await _kiotaUtil.EnsureInstalled(cancellationToken);

        string srcDirectory = Path.Combine(gitDirectory, "src", Constants.Library);

        await DeleteAllExceptCsproj(srcDirectory, cancellationToken);

        await _kiotaUtil.Generate(filePath, "KlaviyoOpenApiClient", Constants.Library, gitDirectory, cancellationToken).NoSync();

        await BuildAndPush(gitDirectory, cancellationToken).NoSync();
    }

    private static ValueTask DeleteAllExceptCsproj(string directoryPath, CancellationToken cancellationToken = default)
    {
        // Only generated source is replaced; retain project files and build assets.
        foreach (string file in Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(directoryPath, file);
            if (relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            File.Delete(file);
        }

        string lockFile = Path.Combine(directoryPath, "kiota-lock.json");
        if (File.Exists(lockFile))
            File.Delete(lockFile);

        return ValueTask.CompletedTask;
    }
    private async ValueTask BuildAndPush(string gitDirectory, CancellationToken cancellationToken)
    {
        string projFilePath = Path.Combine(gitDirectory, "src", Constants.Library, $"{Constants.Library}.csproj");

        await _dotnetUtil.Restore(projFilePath, cancellationToken: cancellationToken);

        bool successful = await _dotnetUtil.Build(projFilePath, true, "Release", false, cancellationToken: cancellationToken);

        if (!successful)
        {
            throw new InvalidOperationException("The regenerated Klaviyo client did not build.");
        }

        bool shouldPush = bool.TryParse(_configuration["Klaviyo:PushChanges"], out bool pushChanges)
            ? pushChanges : _configuration["Klaviyo:LocalRepositoryPath"] == null;
        if (!shouldPush)
            return;

        string gitHubToken = EnvironmentUtil.GetVariableStrict("GH__TOKEN");
        string name = EnvironmentUtil.GetVariableStrict("GIT__NAME");
        string email = EnvironmentUtil.GetVariableStrict("GIT__EMAIL");

        await _gitUtil.CommitAndPush(gitDirectory, "Automated update", gitHubToken, name, email, cancellationToken);
    }
}

