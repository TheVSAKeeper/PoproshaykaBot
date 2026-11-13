namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public static class ControlTreeExtensions
{
    public static IEnumerable<Control> DescendantsAndSelf(this Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        yield return root;

        foreach (Control child in root.Controls)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }
}
