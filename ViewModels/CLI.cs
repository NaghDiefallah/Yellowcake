using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    public void ProcessArgs(string[] args)
    {
        if (args.Length == 0) return;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--path" && i + 1 < args.Length)
            {
                GamePath = args[i + 1];
                Log.Information("Path set via CLI: {Path}", GamePath);
            }
        }
    }
}