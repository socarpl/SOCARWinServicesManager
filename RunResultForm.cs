namespace Socar.WinServicesManager;

public sealed class RunResultForm : Form
{
    public RunResultForm(string resultText)
    {
        Text = "Profile Run Result";
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;

        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Text = resultText
        };

        var closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Dock = DockStyle.Bottom,
            Height = 36
        };

        Controls.Add(textBox);
        Controls.Add(closeButton);
        AcceptButton = closeButton;
    }
}
