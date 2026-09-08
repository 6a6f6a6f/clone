namespace Clone.Git.Exceptions;

public class AmbiguousExecutableNameMatchException(string executable, string[] paths)
    : Exception($"Binary {executable} has multiple matches: {string.Join(", ", paths)}.");
