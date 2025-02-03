using BitbucketRepoBulkCloner.Managers;
using BitbucketRepoBulkCloner.Models;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

class Program
{
    static async Task Main(string[] args)
    {
        // Bitbucket API connection info
        string bitbucketApiBaseUrl = "https://api.bitbucket.org/2.0";
        string bitbucketUsername = "bitbucketUsername";
        string bitbucketAppPassword = "bitbucketAppPassword";
        string workspace = "workspace";

        // ProjectKey List
        var projectKeys = new List<string>
            {
                "projectKey1", "projectKey2", "projectKey3"
            };

        Console.WriteLine("Bitbucket Manager oluşturuluyor...");
        var bitbucketManager = new BitbucketManager(bitbucketApiBaseUrl, bitbucketUsername, bitbucketAppPassword);

        foreach (var projectKey in projectKeys)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {projectKey} PROJECT'İ İÇİN YEDEKLEME BAŞLIYOR ===");

            string localBackupDir = Path.Combine(Environment.CurrentDirectory, $"BitbucketBackup_{projectKey}");
            string zipFilePath = Path.Combine(Environment.CurrentDirectory, $"BitbucketBackup_{projectKey}.zip");

            if (Directory.Exists(localBackupDir))
            {
                Console.WriteLine($"Önceki {localBackupDir} klasörü siliniyor...");
                Directory.Delete(localBackupDir, recursive: true);
            }

            Console.WriteLine($"Workspace: {workspace}, Project Key: {projectKey} altındaki repolar çekiliyor...");
            var repositories = await bitbucketManager.GetRepositoriesByProjectKeyAsync(workspace, projectKey);

            if (repositories == null || repositories.Count == 0)
            {
                Console.WriteLine("Hiç repository bulunamadı veya çekilemedi.");
                continue;
            }

            Console.WriteLine($"{repositories.Count} adet repository bulundu. Klonlama işlemi (paralel) başlıyor...");

            await CloneLocalAndZipRepositoriesParallelAsync(
                repositories,
                localBackupDir,
                zipFilePath,
                bitbucketUsername,
                bitbucketAppPassword,
                maxParallelClones: 5 
            );

            Console.WriteLine("İşlem tamamlandı.");
            Console.WriteLine($"Tüm repository'ler {localBackupDir} içerisine klonlandı ve zip dosyası oluşturuldu: {zipFilePath}");
        }

        Console.WriteLine();
        Console.WriteLine("TÜM PROJECT KEY'LER İÇİN YEDEKLEME TAMAMLANDI.");
        Console.ReadLine();
    }

    /// <summary>
    /// Gelen Bitbucket repository listesini lokal klasöre klonlar (paralel) ve sonrasında zip'ler.
    /// </summary>
    private static async Task CloneLocalAndZipRepositoriesParallelAsync(
        List<BitbucketRepo> repositories,
        string localBackupDir,
        string zipFilePath,
        string username,
        string password,
        int maxParallelClones)
    {
        Directory.CreateDirectory(localBackupDir);

        using var semaphore = new SemaphoreSlim(maxParallelClones);

        var cloneTasks = new List<Task>();

        foreach (var repo in repositories)
        {
            cloneTasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await CloneSingleRepoAsync(repo, localBackupDir, username, password);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(cloneTasks);

        Console.WriteLine("Tüm repolar klonlandı. Şimdi zip dosyası oluşturuluyor...");

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }

        ZipFile.CreateFromDirectory(localBackupDir, zipFilePath, CompressionLevel.Optimal, includeBaseDirectory: true);

        Console.WriteLine($"Zip dosyası oluşturuldu: {zipFilePath}");
    }

    /// <summary>
    /// Tek bir repo'yu klonlamak için asenkron fonksiyon.
    /// </summary>
    private static async Task CloneSingleRepoAsync(BitbucketRepo repo, string localBackupDir, string username, string password)
    {
        string repoLocalPath = Path.Combine(localBackupDir, repo.Name);

        if (repo.CloneUrl == null)
        {
            Console.WriteLine($"Hata: {repo.Name} için HTTPS clone URL bulunamadı. Atlanıyor.");
            return;
        }

        var uriBuilder = new UriBuilder(repo.CloneUrl)
        {
            UserName = username,
            Password = password
        };

        string cloneUrlWithCreds = uriBuilder.Uri.ToString();

        Console.WriteLine($"[PARALEL] {repo.Name} klonlaması başlıyor => {repoLocalPath}");

        await RunGitCommandAsync($"clone --mirror \"{cloneUrlWithCreds}\" \"{repoLocalPath}\"");

        Console.WriteLine($"[PARALEL] {repo.Name} klonlaması tamamlandı.");
    }

    /// <summary>
    /// Komut satırında git komutu çalıştırır.
    /// </summary>
    private static async Task RunGitCommandAsync(string arguments)
    {
        Console.WriteLine($"[GIT CMD] git {arguments}");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = new Process { StartInfo = processStartInfo })
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    outputBuilder.AppendLine($"[OUT] {e.Data}");
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    errorBuilder.AppendLine($"[ERR] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Console.WriteLine("GIT komutu başarısız oldu!");
                Console.WriteLine("Çıktı:");
                Console.WriteLine(outputBuilder.ToString());
                Console.WriteLine("Hata:");
                Console.WriteLine(errorBuilder.ToString());
            }
            else
            {
                // Debug amaçlı 
                Console.WriteLine(outputBuilder.ToString());
                Console.WriteLine(errorBuilder.ToString());

                Console.WriteLine("GIT komutu başarıyla tamamlandı.");
            }
        }
    }
}