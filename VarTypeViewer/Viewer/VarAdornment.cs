using System.Windows.Controls;

namespace VarTypeViewer.Viewer
{
    // todo styling from TextViewer
    internal sealed class VarAdornment : TextBlock
    {
        internal VarAdornment(VarTag varTag)
        {
            Update(varTag);
        }

        internal void Update(VarTag varTag)
        {
            Text = $"<{varTag.TypeName}>";
        }
    }
}
