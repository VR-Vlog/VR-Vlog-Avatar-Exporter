namespace VRVlog.LilToonExporter
{
    // The last file the board saved, with what it holds.
    internal sealed class SavedExpressionVrm
    {
        internal string Path { get; }
        internal int Expressions { get; }
        internal int Poses { get; }
        // Materials whose shader was missing from the project when this file was saved.
        internal int MissingShaders { get; }
        internal SavedExpressionVrm(string path, int expressions, int poses, int missingShaders)
        {
            Path = path; Expressions = expressions; Poses = poses; MissingShaders = missingShaders;
        }
    }
}
