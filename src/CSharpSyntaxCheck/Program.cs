using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

var optionsResult = Options.Parse(args);
if (!optionsResult.Success)
{
    Console.Error.WriteLine(optionsResult.Error);
    Console.Error.WriteLine();
    Options.PrintUsage();
    return 2;
}

var options = optionsResult.Value;
if (options.ShowHelp)
{
    Options.PrintUsage();
    return 0;
}

var repoRoot = Path.GetFullPath(options.Path);
if (!Directory.Exists(repoRoot))
{
    Console.Error.WriteLine($"Repository path does not exist: {repoRoot}");
    return 2;
}

if (!LanguageVersionFacts.TryParse(options.LanguageVersion, out var languageVersion))
{
    Console.Error.WriteLine($"Unsupported C# language version: {options.LanguageVersion}");
    return 2;
}

var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
var filesChecked = 0;
var errorCount = 0;

foreach (var filePath in EnumerateCSharpFiles(repoRoot, options.ExcludedDirectories))
{
    filesChecked++;

    var source = await File.ReadAllTextAsync(filePath);
    var tree = CSharpSyntaxTree.ParseText(source, parseOptions, filePath);
    var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    foreach (var error in errors)
    {
        errorCount++;

        var lineSpan = error.Location.GetLineSpan();
        var position = lineSpan.StartLinePosition;
        var relativePath = Path.GetRelativePath(repoRoot, filePath);
        var line = position.Line + 1;
        var column = position.Character + 1;
        var message = error.GetMessage();

        Console.Error.WriteLine($"{relativePath}({line},{column}): {error.Id}: {message}");
        Console.Error.WriteLine($"::error file={EscapeAnnotationProperty(relativePath)},line={line},col={column},title={EscapeAnnotationProperty(error.Id)}::{EscapeAnnotationData(message)}");
    }
}

if (filesChecked == 0)
{
    var message = "No C# files were found to check.";
    if (options.FailOnEmpty)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    Console.WriteLine(message);
    return 0;
}

if (errorCount > 0)
{
    Console.Error.WriteLine($"C# syntax check failed: {errorCount} error(s) across {filesChecked} file(s).");
    return 1;
}

Console.WriteLine($"C# syntax check passed: {filesChecked} file(s) parsed.");
return 0;

static IEnumerable<string> EnumerateCSharpFiles(string root, IReadOnlyCollection<string> excludedDirectories)
{
    var pendingDirectories = new Stack<DirectoryInfo>();
    pendingDirectories.Push(new DirectoryInfo(root));

    while (pendingDirectories.Count > 0)
    {
        var currentDirectory = pendingDirectories.Pop();

        FileInfo[] files;
        try
        {
            files = currentDirectory.GetFiles("*.cs", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            Console.Error.WriteLine($"Skipping unreadable directory: {currentDirectory.FullName}");
            continue;
        }

        foreach (var file in files)
        {
            yield return file.FullName;
        }

        DirectoryInfo[] childDirectories;
        try
        {
            childDirectories = currentDirectory.GetDirectories("*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            Console.Error.WriteLine($"Skipping unreadable directory: {currentDirectory.FullName}");
            continue;
        }

        foreach (var childDirectory in childDirectories)
        {
            if (ShouldSkipDirectory(root, childDirectory, excludedDirectories))
            {
                continue;
            }

            if ((childDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            pendingDirectories.Push(childDirectory);
        }
    }
}

static bool ShouldSkipDirectory(string root, DirectoryInfo directory, IReadOnlyCollection<string> excludedDirectories)
{
    var relativePath = Path.GetRelativePath(root, directory.FullName)
        .Replace(Path.DirectorySeparatorChar, '/')
        .Replace(Path.AltDirectorySeparatorChar, '/');

    foreach (var excludedDirectory in excludedDirectories)
    {
        if (string.Equals(directory.Name, excludedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(relativePath, excludedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (relativePath.StartsWith(excludedDirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static string EscapeAnnotationProperty(string value)
{
    return EscapeAnnotationData(value).Replace(",", "%2C").Replace(":", "%3A");
}

static string EscapeAnnotationData(string value)
{
    return value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
}

internal sealed record Options(
    string Path,
    string LanguageVersion,
    IReadOnlyCollection<string> ExcludedDirectories,
    bool FailOnEmpty,
    bool ShowHelp)
{
    private static readonly string[] DefaultExcludedDirectories =
    {
        ".git",
        ".vs",
        "bin",
        "Build",
        "Builds",
        "Library",
        "Logs",
        "obj",
        "Temp",
        "UserSettings"
    };

    public static ParseResult Parse(string[] args)
    {
        var path = ".";
        var languageVersion = "preview";
        var excludedDirectories = DefaultExcludedDirectories.ToList();
        var failOnEmpty = true;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];

            switch (arg)
            {
                case "--help":
                case "-h":
                    return ParseResult.Ok(new Options(path, languageVersion, excludedDirectories, failOnEmpty, true));

                case "--path":
                    if (!TryReadValue(args, ref index, arg, out path))
                    {
                        return ParseResult.Fail($"{arg} requires a value.");
                    }
                    break;

                case "--language-version":
                    if (!TryReadValue(args, ref index, arg, out languageVersion))
                    {
                        return ParseResult.Fail($"{arg} requires a value.");
                    }
                    break;

                case "--exclude-directories":
                    if (!TryReadValue(args, ref index, arg, out var excludeValue))
                    {
                        return ParseResult.Fail($"{arg} requires a value.");
                    }

                    excludedDirectories = SplitList(excludeValue).ToList();
                    break;

                case "--exclude-directory":
                    if (!TryReadValue(args, ref index, arg, out var directory))
                    {
                        return ParseResult.Fail($"{arg} requires a value.");
                    }

                    excludedDirectories.Add(NormalizeExcludedDirectory(directory));
                    break;

                case "--fail-on-empty":
                    failOnEmpty = true;
                    break;

                case "--no-fail-on-empty":
                    failOnEmpty = false;
                    break;

                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        return ParseResult.Fail($"Unknown option: {arg}");
                    }

                    path = arg;
                    break;
            }
        }

        return ParseResult.Ok(new Options(path, languageVersion, excludedDirectories, failOnEmpty, false));
    }

    public static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: CSharpSyntaxCheck [--path <path>] [--language-version <version>] [--exclude-directories <list>] [--no-fail-on-empty]");
    }

    private static bool TryReadValue(string[] args, ref int index, string optionName, out string value)
    {
        value = string.Empty;

        if (index + 1 >= args.Length)
        {
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value) && !value.StartsWith("-", StringComparison.Ordinal);
    }

    private static IEnumerable<string> SplitList(string value)
    {
        return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeExcludedDirectory)
            .Where(static directory => directory.Length > 0);
    }

    private static string NormalizeExcludedDirectory(string directory)
    {
        return directory.Trim()
            .Trim('/')
            .Replace('\\', '/');
    }
}

internal sealed record ParseResult(bool Success, Options Value, string Error)
{
    public static ParseResult Ok(Options options) => new(true, options, string.Empty);

    public static ParseResult Fail(string error) => new(false, new Options(".", "preview", Array.Empty<string>(), true, false), error);
}
