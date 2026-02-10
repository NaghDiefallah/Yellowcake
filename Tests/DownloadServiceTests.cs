using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Octokit;
using Xunit;
using Yellowcake.Services;
using ProductHeaderValue = Octokit.ProductHeaderValue;

namespace Yellowcake.Tests;

public class DownloadServiceTests
{
    [Fact]
    public async Task ResolveDirectLink_GithubBlob_ConvertsToRaw()
    {
        // Arrange: mock HttpMessageHandler to return a successful response for the actual download
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("file-content")
                };
                resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileNameStar = "file.txt"
                };
                return resp;
            });

        var http = new HttpClient(handler.Object);
        var gh = new GitHubClient(new ProductHeaderValue("yellowcake-tests"));
        var svc = new DownloadService(http, gh);

        // Act
        var result = await svc.UniversalDownloadAsync("https://github.com/test/repo/blob/main/file.txt");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("/raw/", result.FinalUrl);
        Assert.NotNull(result.Stream);
        Assert.Equal("file.txt", result.SuggestedFileName);
    }

    [Fact]
    public async Task GoogleDrive_ConfirmFlow_ReturnsBinaryAndSuggestedFilename()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var uri = req.RequestUri!.AbsoluteUri;

                // initial uc?export=download request -> returns HTML with confirm token
                if (uri.Contains("uc?export=download") && !uri.Contains("confirm="))
                {
                    var html = @"<html><a href=""/uc?export=download&amp;confirm=TOKEN123&amp;id=FILEID"">download</a></html>";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(html, Encoding.UTF8, "text/html")
                    };
                }

                // confirmed URL with token -> return binary and Content-Disposition
                if (uri.Contains("confirm=TOKEN123"))
                {
                    var bytes = Encoding.UTF8.GetBytes("binary-content");
                    var resp = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(bytes)
                    };
                    resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "drivefile.dll" };
                    return resp;
                }

                // Fallback
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        var http = new HttpClient(handler.Object);
        var gh = new GitHubClient(new ProductHeaderValue("yellowcake-tests"));
        var svc = new DownloadService(http, gh);

        // Act
        var result = await svc.UniversalDownloadAsync("https://drive.google.com/file/d/FILEID/view?usp=sharing");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("confirm=", result.FinalUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("drivefile.dll", result.SuggestedFileName);
        Assert.NotEqual(0, result.Stream.Length);
    }

    [Fact]
    public async Task VerifyHash_SucceedsForMatchingHash()
    {
        // Arrange: content "hello"
        byte[] content = Encoding.UTF8.GetBytes("hello");
        string hash;
        using (var sha = SHA256.Create())
        {
            hash = Convert.ToHexString(sha.ComputeHash(content));
        }

        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });

        var http = new HttpClient(handler.Object);
        var gh = new GitHubClient(new ProductHeaderValue("yellowcake-tests"));
        var svc = new DownloadService(http, gh);

        // Act
        var result = await svc.UniversalDownloadAsync("https://example.com/file.bin", expectedHash: hash);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(0, result.Stream.Length);
    }

    [Fact]
    public async Task VerifyHash_ThrowsOnMismatch()
    {
        // Arrange: content "hello"
        byte[] content = Encoding.UTF8.GetBytes("hello");
        string badHash = new string('A', 64);

        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });

        var http = new HttpClient(handler.Object);
        var gh = new GitHubClient(new ProductHeaderValue("yellowcake-tests"));
        var svc = new DownloadService(http, gh);

        // Act & Assert
        await Assert.ThrowsAsync<System.Security.SecurityException>(async () =>
        {
            await svc.UniversalDownloadAsync("https://example.com/file.bin", expectedHash: badHash);
        });
    }
}