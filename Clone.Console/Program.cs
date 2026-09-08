if (args.Length != 1)
{
    Printer.PrintError("You must call Router with only one command line argument!");
    Environment.Exit(1);
}

var remote = args[0];
if (!Turtle.TryParseRemote(remote, out var user, out var repository))
{
    Printer.PrintError("Invalid remote format!");
    Environment.Exit(1);
}

var projectFolder = Environment.GetEnvironmentVariable("CLONE_PROJECT_FOLDER");
if (!Directory.Exists(projectFolder))
{
    Printer.PrintWarning("Project folder does not exist!");
    Printer.PrintInformational("Set `CLONE_PROJECT_FOLDER` with root folder for clones.");
    Environment.Exit(1);
}

var destinationFolder = Path.Combine(projectFolder, user);
if (!Directory.Exists(destinationFolder)) Directory.CreateDirectory(destinationFolder);

var cloneFolder = Path.Combine(destinationFolder, repository);
if (Directory.Exists(cloneFolder))
{
    Printer.PrintWarning("Repository already exists!");
    Printer.PrintInformational("You may want to check the docs to improve your usage.");
    Environment.Exit(1);
}

var errors = new List<string>();
Turtle.Clone(remote, cloneFolder, output =>
{
    if (output.StartsWith("ERROR:"))
    {
        errors.Add(output);
        return;
    }

    Printer.PrintGitStdout(output);
});

if (errors.Count != 0)
{
    Printer.PrintError("Git had problems cloning your remote: ");
    errors.ForEach(Printer.PrintError);

    var files = Directory.GetDirectories(destinationFolder);
    if (files.Length == 0) Directory.Delete(destinationFolder);

    Environment.Exit(1);
}

Printer.PrintSuccess("Repository cloned!");
