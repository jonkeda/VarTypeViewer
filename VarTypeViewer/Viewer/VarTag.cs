using Microsoft.VisualStudio.Text.Tagging;

namespace VarTypeViewer.Viewer
{
    internal class VarTag : ITag
    {
        internal VarTag(string typeName)
        {
            TypeName = typeName;
        }

        internal readonly string TypeName;
    }
}
