using Avalonia;
using System;

class Program
{
    static void Main()
    {
        var options = new MacOSPlatformOptions();
        Console.WriteLine("Options available:");
        foreach (var prop in typeof(MacOSPlatformOptions).GetProperties())
        {
            Console.WriteLine(prop.Name);
        }
    }
}
