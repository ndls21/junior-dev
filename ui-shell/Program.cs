using System;
using System.Windows.Forms;

namespace Ui.Shell;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // TODO: Implement UI shell
        Application.Run(new Form()); // Placeholder
    }
}