using System.Net;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Tar;
using Vox.Core.Models;

namespace Vox.Core.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("vox-models-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Leftovers_of_an_interrupted_install_are_removed()
    {
        // Regression: closing the wizard during extraction left the archive and a half-extracted folder.
        var id = ModelCatalog.GigaAm.Id;
        File.WriteAllText(Path.Combine(_root, $"{id}-0123abcd.tar.bz2"), "partial");
        Directory.CreateDirectory(Path.Combine(_root, $"{id}-stage-0123abcd", "half"));
        var installed = Directory.CreateDirectory(ModelCatalog.DirectoryOf(ModelCatalog.Parakeet, _root)).FullName;
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not ours");

        ModelDownloader.RemoveLeftovers(_root);

        Assert.Equal(["notes.txt"], Directory.GetFiles(_root).Select(Path.GetFileName));
        Assert.Equal([installed], Directory.GetDirectories(_root));
    }

    [Fact]
    public void Missing_models_folder_is_fine() => ModelDownloader.RemoveLeftovers(Path.Combine(_root, "absent"));

    [Fact]
    public async Task Archive_that_matches_its_pin_is_installed()
    {
        var fixture = new Fixture();

        await Install(fixture.Model, new Server(fixture.Archive));

        Assert.True(ModelFiles.IsIntact(fixture.Model, _root, rejectLinks: false));
        Assert.Equal([ModelCatalog.DirectoryOf(fixture.Model, _root)], Directory.GetDirectories(_root));
        Assert.Empty(Directory.GetFiles(_root)); // the archive is gone
    }

    [Fact]
    public async Task Archive_with_one_changed_byte_is_refused_and_deleted()
    {
        var fixture = new Fixture();
        var changed = fixture.Archive.ToArray();
        changed[^1] ^= 0x01;

        await Assert.ThrowsAsync<ModelIntegrityException>(() => Install(fixture.Model, new Server(changed)));

        AssertNothingLeft();
    }

    [Fact]
    public async Task Cut_download_without_content_length_is_refused()
    {
        // A server that sends no Content-Length: the old check never compared the size, so a cut
        // archive went straight to the unpacker.
        var fixture = new Fixture();

        var error = await Assert.ThrowsAsync<IOException>(
            () => Install(fixture.Model, new Server(fixture.Archive[..^10], sendLength: false)));

        Assert.StartsWith("Загрузка оборвалась", error.Message, StringComparison.Ordinal);
        AssertNothingLeft();
    }

    [Fact]
    public async Task Download_longer_than_the_pin_is_stopped()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ModelIntegrityException>(
            () => Install(fixture.Model, new Server([.. fixture.Archive, .. new byte[1000]], sendLength: false)));

        AssertNothingLeft();
    }

    [Fact]
    public async Task Wrong_content_length_is_refused()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ModelIntegrityException>(
            () => Install(fixture.Model, new Server([.. fixture.Archive, 0])));

        AssertNothingLeft();
    }

    [Fact]
    public async Task Planted_model_folder_is_replaced_not_trusted()
    {
        // M1: another user creates the folder first, with a tokens.txt of their own.
        var fixture = new Fixture();
        var planted = Directory.CreateDirectory(ModelCatalog.DirectoryOf(fixture.Model, _root)).FullName;
        foreach (var pin in fixture.Model.Files)
        {
            File.WriteAllText(Path.Combine(planted, pin.Path), "planted");
        }

        var server = new Server(fixture.Archive);
        await Install(fixture.Model, server);

        Assert.Equal(1, server.Requests);
        Assert.True(ModelFiles.IsIntact(fixture.Model, _root, rejectLinks: false));
    }

    [Fact]
    public async Task Intact_model_is_not_downloaded_again()
    {
        var fixture = new Fixture();
        await Install(fixture.Model, new Server(fixture.Archive));
        var server = new Server(fixture.Archive);

        await Install(fixture.Model, server);

        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task Archive_whose_files_differ_from_their_pins_is_not_installed()
    {
        // A wrong file pin in the catalog: caught in the staging folder, before the model is moved in.
        var fixture = new Fixture();
        var model = fixture.Model with { Files = [.. fixture.Model.Files.Select(p => p with { Sha256 = new string('0', 64) })] };

        await Assert.ThrowsAsync<ModelIntegrityException>(() => Install(model, new Server(fixture.Archive)));

        AssertNothingLeft();
    }

    private async Task Install(SpeechModel model, Server server)
    {
        using var http = new HttpClient(server);
        await new ModelDownloader(http, _root).InstallAsync(model, null, CancellationToken.None);
    }

    private void AssertNothingLeft() => Assert.Empty(Directory.EnumerateFileSystemEntries(_root));

    /// <summary>A tiny model archive and a <see cref="SpeechModel"/> pinned to it.</summary>
    internal sealed class Fixture
    {
        public Fixture()
        {
            Files =
            [
                ("encoder.onnx", Encoding.UTF8.GetBytes("encoder bytes")),
                ("decoder.onnx", Encoding.UTF8.GetBytes("decoder bytes")),
                ("joiner.onnx", Encoding.UTF8.GetBytes("joiner bytes")),
                ("tokens.txt", Encoding.UTF8.GetBytes("<blk> 0\nа 1\n")),
            ];
            Archive = Pack(Files);
            Model = ModelCatalog.GigaAm with
            {
                Id = "fixture",
                Url = new Uri("https://models.test/fixture.tar.bz2"),
                Archive = new PinnedFile("fixture.tar.bz2", Archive.Length, Sha256(Archive)),
                Files = [.. Files.Select(f => new PinnedFile(f.Name, f.Data.Length, Sha256(f.Data)))],
                ExtractedDir = "fixture-model",
                Encoder = "encoder.onnx",
                Decoder = "decoder.onnx",
                Joiner = "joiner.onnx",
                Tokens = "tokens.txt",
            };
        }

        public (string Name, byte[] Data)[] Files { get; }

        public byte[] Archive { get; }

        public SpeechModel Model { get; }

        private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

        private static byte[] Pack((string Name, byte[] Data)[] files)
        {
            using var output = new MemoryStream();
            using (var bz = new BZip2OutputStream(output) { IsStreamOwner = false })
            using (var tar = new TarOutputStream(bz, Encoding.UTF8))
            {
                foreach (var (name, data) in files)
                {
                    var entry = TarEntry.CreateTarEntry($"fixture-model/{name}");
                    entry.Size = data.Length;
                    tar.PutNextEntry(entry);
                    tar.Write(data);
                    tar.CloseEntry();
                }
            }

            return output.ToArray();
        }
    }

    /// <summary>Answers every request with <paramref name="body"/>, with or without Content-Length.</summary>
    private sealed class Server(byte[] body, bool sendLength = true) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            HttpContent content = sendLength ? new ByteArrayContent(body) : new UnknownLength(body);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class UnknownLength(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
