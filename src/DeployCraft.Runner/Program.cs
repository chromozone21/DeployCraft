using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace DeployCraft.Runner
{
    internal static class Program
    {
        /// <summary>
        /// Lancé par le gestionnaire de services : le dossier de travail est celui de l'exe.
        /// Pour tester à la main : DeployCraft.Runner.exe --console [dossier]
        /// </summary>
        private static int Main(string[] args)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;

            if (args.Length > 0 && args[0] == "--console")
            {
                var dir = args.Length > 1 ? Path.GetFullPath(args[1]) : exeDir;
                new StepExecutor(dir).Run();
                Console.WriteLine(File.ReadAllText(Path.Combine(dir, "status.json")));
                return 0;
            }

            ServiceBase.Run(new RunnerService(exeDir));
            return 0;
        }
    }

    internal class RunnerService : ServiceBase
    {
        private readonly StepExecutor _executor;
        private Thread _worker;
        private volatile bool _finished;

        public RunnerService(string slotDir)
        {
            _executor = new StepExecutor(slotDir);
            CanStop = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            // OnStart doit rendre la main vite : le travail se fait dans un thread.
            _worker = new Thread(() =>
            {
                _executor.Run();
                _finished = true;
                // Le service s'arrête tout seul ; le serveur s'occupe ensuite de le supprimer.
                Stop();
            })
            { IsBackground = true, Name = "DeployCraftWorker" };
            _worker.Start();
        }

        /// <summary>Arrêt demandé par le serveur avant la fin = annulation du déploiement sur ce poste.</summary>
        protected override void OnStop()
        {
            if (_finished) return;
            _executor.Abort();
            _worker?.Join(TimeSpan.FromSeconds(15));
        }
    }
}
