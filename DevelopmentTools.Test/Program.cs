using System;
using System.Collections.Generic;

namespace DevelopmentTools.Test
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try
            {
                System.IO.Directory.SetCurrentDirectory(@"C:\Program Files\Autodesk\Revit 2024");
                var asm = System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll");
                var type = asm.GetType("Autodesk.Revit.UI.UIView");
                Console.WriteLine("UIView Members:");
                foreach (var member in type.GetMembers())
                {
                    if (member.Name.Contains("Matrix") || member.Name.Contains("Camera") || member.Name.Contains("Rectangle") || member.Name.Contains("View"))
                    {
                        Console.WriteLine($"  {member.Name} : {member.MemberType}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Reflection failed: " + ex.Message);
            }
        }
    }
}
