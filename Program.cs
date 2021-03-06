﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Drawing;
using System.Runtime.InteropServices;

namespace forever {

  class Program {
    public static volatile bool _stop = false;
    // This isn't really correct from a thread perspective,
    // but worst case scenario a crashed app will be immediately restarted,
    // so it should be fine.
    public static volatile bool _restartWatch = false;

    public static Thread[] _runningWatchers;
    public static string[] _runningProcessNames;
    public static string _watchFileName;
    public static int _watchFileSeconds = 20;

    public static bool WatchersRunning {
      get {
        foreach(Thread t in _runningWatchers) {
          if(t.IsAlive) {
            return true;
          }
        }
        return false;
      }
    }

    static void Main(string[] args) {
      if(args.Length <= 1) {
        Usage();
        return;
      }

      // yeah this is lame.
      // miss single file header style libraries,
      // too lazy to figure out how to do chained library for c#
      // will regret probably
      int watchFileDelay = 60;
      int startProgramList = args.Length;
      for(int pArg = 0; pArg < args.Length; pArg++) {
        
        if(!args[pArg].StartsWith("--")) {
          startProgramList = pArg;
          break;
        } else if(args[pArg].StartsWith("--watch_file=", StringComparison.CurrentCultureIgnoreCase)) {
          _watchFileName = args[pArg].Substring("--watch_file=".Length);
        } else if(args[pArg].StartsWith("--watch_file_seconds=", StringComparison.CurrentCultureIgnoreCase)) {
          string numSeconds = args[pArg].Substring("--watch_file_seconds=".Length);
          _watchFileSeconds = Int32.Parse(numSeconds);
        } else if(args[pArg].StartsWith("--watch_file_delay=", StringComparison.CurrentCultureIgnoreCase)) {
          string numSeconds = args[pArg].Substring("--watch_file_delay=".Length);
          watchFileDelay = Int32.Parse(numSeconds);
        }
      }

      int numPrograms = (args.Length - startProgramList) / 2;

      if(numPrograms <= 0) {
        Usage();
        return;
      }

      _runningWatchers = new Thread[numPrograms];
      _runningProcessNames = new string[numPrograms];

      for(int p = 0; p < numPrograms; p++) {
        int programIndex = startProgramList + (p * 2);
        _runningProcessNames[p] = System.IO.Path.GetFileNameWithoutExtension(args[programIndex]); 
        StartAndWatch(args[programIndex], args[programIndex + 1], ref _runningWatchers[p]);
      }

      if(_watchFileName.Length > 2) {
        StartFileWatch(watchFileDelay);
      }

      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      SysTrayContext stContext = new SysTrayContext();
      Application.Run(stContext);

    }

    static void StartFileWatch(int startDelay) {

      Thread thread = new Thread(delegate() {
        
        while(true) {

          if(_stop) {
            break;
          }

          if(_restartWatch) {
            _restartWatch = false;
            Thread.Sleep(startDelay * 1000);
            continue;
          }

          bool killAsWriteNeverHappened = false;
          DateTime lastTime = DateTime.Now;
          try {
            lastTime = System.IO.File.GetLastWriteTime(_watchFileName);
          } catch(Exception) {
            killAsWriteNeverHappened = true;
          }

          // if it hasn't changed in _watchFileSeconds (roughly since we last checked), assume it crashed
          if(killAsWriteNeverHappened || DateTime.Now.Subtract(lastTime).Seconds > _watchFileSeconds) {
            foreach(string processName in _runningProcessNames) {
              Process[] localByName = Process.GetProcessesByName(processName);
              foreach(Process p in localByName) {
                p.Kill();
              }
            }

            // sleep again 
            Thread.Sleep(startDelay * 1000);
          } else {
            Thread.Sleep(_watchFileSeconds * 1000);
          }
        }
      });
      thread.Start();
    }
    
    static void Usage() {
      MessageBox.Show(
          "usage:\n\nforever \n"
              + "--watchFile=<filename>\n"
              + "--watchFileSeconds=<seconds>\n"
              + "--watchFileDelay=<seconds>\n"
              + "<application1> \"<arguments1>\" [<application2> \"<arguments2>\"] ..",
          "forever usage",
          MessageBoxButtons.OK,
          MessageBoxIcon.Information,
          MessageBoxDefaultButton.Button1
      );
    }

    static void StartAndWatch(
        string fileName, string fileArgs, ref Thread thread) {

      thread = new Thread(delegate() {
        var psi = new ProcessStartInfo {
          FileName = fileName,
          Arguments = fileArgs,
          CreateNoWindow = true,
          ErrorDialog = false,
          UseShellExecute = false

        };

        int runsPerSecond = 0;
        var rpsStowwatch = Stopwatch.StartNew();

        while(true) {
          if(_stop) {
            break;
          }
          if(runsPerSecond > 10) {
            if(MessageBox.Show(
                String.Format("The process '{0}' with the arguments '{1}' got restarted more than 10 times in one second.\n" +
                "This tends to be a unwanted behavior.\n\nIf you press cancel the other processes will continue to be watched.", fileName, fileArgs),
                "something is not right",
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Error) == DialogResult.Retry) {
            } else {
              break;
            }
          }
          try {
            if(System.Environment.HasShutdownStarted)
              break;
            if(rpsStowwatch.ElapsedMilliseconds > 1000) {
              rpsStowwatch.Reset();
              runsPerSecond = 0;
            }
            runsPerSecond++;
            _restartWatch = true;
            int oldMode = SetErrorMode(3);
            var p = Process.Start(psi);
            SetErrorMode(oldMode);
            p.WaitForExit();
          } catch(Exception x) {
            MessageBox.Show(
                String.Format("Error while running arguments.\n\nError:\n  {0}\nFilename:\n  {1}\nArguments:\n  {2}",
                    x.Message, fileName, fileArgs),
                "Error while running arguments",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            break;
          }
        }
      });
      thread.Start();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern int SetErrorMode(int wMode);
  }
}
