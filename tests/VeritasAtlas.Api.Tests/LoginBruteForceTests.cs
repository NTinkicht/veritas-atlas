using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Contracts.Auth;
using VeritasAtlas.Api.Controllers;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Tests;

/// <summary>VER-P0-02 brute-force, rate-limit and login-input vectors (#73).</summary>
[TestClass]
public sealed class LoginBruteForceTests
{
    private const string StagingUser = "stagingadmin";
    private const string StagingPassword = "an-independent-staging-secret-1234";

    // ---- per-client request limiter -------------------------------------

    [TestMethod]
    public void LimiterIsPartitionedPerClientNotGlobal()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            LoginRateLimiting.Partition);
        var attacker = Client("203.0.113.7");
        var victim = Client("198.51.100.20");

        for (var i = 0; i < LoginRateLimiting.PermitLimit; i++)
        {
            using var lease = limiter.AttemptAcquire(attacker);
            Assert.IsTrue(lease.IsAcquired, $"request {i + 1} is within the per-client budget");
        }
        using (var over = limiter.AttemptAcquire(attacker))
        {
            Assert.IsFalse(over.IsAcquired, "the 7th request in the window is throttled");
        }
        using var other = limiter.AttemptAcquire(victim);
        Assert.IsTrue(other.IsAcquired, "another client keeps its own login budget");
    }

    [TestMethod]
    public void PartitionKeyUsesRemoteAddressAndFailsToSharedBucket()
    {
        Assert.AreEqual("203.0.113.7", LoginRateLimiting.PartitionKey(Client("203.0.113.7")));
        Assert.AreEqual("unknown", LoginRateLimiting.PartitionKey(new DefaultHttpContext()));
    }

    // ---- per-account lockout -------------------------------------------

    [TestMethod]
    public void AccountLocksAfterMaxFailuresEvenWithCorrectPassword()
    {
        var (controller, _) = Controller();
        for (var i = 0; i < LoginAttemptGuard.MaxFailures; i++)
        {
            AssertStatus(HttpStatusCode.Unauthorized, controller.Login(new("operator1", "wrong-" + i)));
        }
        AssertStatus(HttpStatusCode.TooManyRequests, controller.Login(new("operator1", "password123")));
        // Case/whitespace variants of the same account are the same account.
        AssertStatus(HttpStatusCode.TooManyRequests, controller.Login(new("  OPERATOR1 ", "password123")));
        // Other accounts are unaffected.
        AssertStatus(HttpStatusCode.OK, controller.Login(new("reviewer1", "password123")));
    }

    [TestMethod]
    public void FailuresBelowThresholdDoNotLockAndSuccessResetsCounter()
    {
        var (controller, _) = Controller();
        for (var i = 0; i < LoginAttemptGuard.MaxFailures - 1; i++)
        {
            AssertStatus(HttpStatusCode.Unauthorized, controller.Login(new("admin1", "nope")));
        }
        AssertStatus(HttpStatusCode.OK, controller.Login(new("admin1", "password123")));
        for (var i = 0; i < LoginAttemptGuard.MaxFailures - 1; i++)
        {
            AssertStatus(HttpStatusCode.Unauthorized, controller.Login(new("admin1", "nope")));
        }
        AssertStatus(HttpStatusCode.OK, controller.Login(new("admin1", "password123")));
    }

    [TestMethod]
    public void LockoutExpiresExactlyAtWindowEnd()
    {
        var (controller, clock) = Controller();
        for (var i = 0; i < LoginAttemptGuard.MaxFailures; i++)
        {
            controller.Login(new("publisher1", "bad"));
        }
        clock.Advance(LoginAttemptGuard.FailureWindow - TimeSpan.FromSeconds(1));
        AssertStatus(HttpStatusCode.TooManyRequests, controller.Login(new("publisher1", "password123")));
        clock.Advance(TimeSpan.FromSeconds(1));
        AssertStatus(HttpStatusCode.OK, controller.Login(new("publisher1", "password123")));
    }

    [TestMethod]
    public void FailuresOutsideTheWindowDoNotAccumulate()
    {
        var clock = new ManualClock();
        var guard = new LoginAttemptGuard(clock);
        for (var i = 0; i < LoginAttemptGuard.MaxFailures - 1; i++) guard.TryReserveAttempt("x");
        clock.Advance(LoginAttemptGuard.FailureWindow);
        Assert.IsTrue(guard.TryReserveAttempt("x"));
        Assert.IsFalse(guard.IsLockedOut("x"), "old failures start a fresh window");
    }

    [TestMethod]
    public void ConcurrentAttemptsForOneAccountCannotExceedThreshold()
    {
        var (controller, _) = Controller();
        const int requests = 64;
        var statuses = new int[requests];
        using var start = new ManualResetEventSlim();
        var workers = Enumerable.Range(0, requests).Select(i => Task.Run(() =>
        {
            start.Wait();
            statuses[i] = StatusOf(controller.Login(new("operator1", "wrong-" + i)));
        })).ToArray();
        start.Set();
        Task.WaitAll(workers);

        Assert.AreEqual(LoginAttemptGuard.MaxFailures,
            statuses.Count(x => x == StatusCodes.Status401Unauthorized),
            "only MaxFailures credential checks may run in one window");
        Assert.AreEqual(requests - LoginAttemptGuard.MaxFailures,
            statuses.Count(x => x == StatusCodes.Status429TooManyRequests));
        AssertStatus(HttpStatusCode.TooManyRequests, controller.Login(new("operator1", "password123")));
    }

    [TestMethod]
    public void RotatingUnknownUsernamesCannotGrowMemoryBeyondCap()
    {
        var clock = new ManualClock();
        var guard = new LoginAttemptGuard(clock, maxTrackedAccounts: 100);
        for (var i = 0; i < 1_000; i++)
        {
            Assert.IsTrue(guard.TryReserveAttempt("spray-" + i));
            Assert.IsTrue(guard.TrackedAccountCount <= 100);
        }
        Assert.AreEqual(100, guard.TrackedAccountCount);
    }

    [TestMethod]
    public void ExpiredRecordsAreSweptBeforeLiveOnesAreEvicted()
    {
        var clock = new ManualClock();
        var guard = new LoginAttemptGuard(clock, maxTrackedAccounts: 10);
        for (var i = 0; i < LoginAttemptGuard.MaxFailures; i++) guard.TryReserveAttempt("victim");
        clock.Advance(TimeSpan.FromMinutes(10));
        for (var i = 0; i < 8; i++) guard.TryReserveAttempt("live-" + i);
        guard.TryReserveAttempt("fresh-0");
        Assert.AreEqual(10, guard.TrackedAccountCount, "table is full");
        Assert.IsTrue(guard.IsLockedOut("victim"));
        clock.Advance(TimeSpan.FromMinutes(6));
        // victim's window is over, live-* still have 9 minutes: the full table
        // sweeps the expired record instead of evicting a live one.
        guard.TryReserveAttempt("fresh-1");
        Assert.AreEqual(10, guard.TrackedAccountCount);
        Assert.IsFalse(guard.IsLockedOut("victim"));
        for (var i = 0; i < LoginAttemptGuard.MaxFailures - 1; i++)
        {
            Assert.IsTrue(guard.TryReserveAttempt("live-0"));
        }
        Assert.IsFalse(guard.TryReserveAttempt("live-0"), "live record kept its earlier attempt");
    }

    [TestMethod]
    public void UnknownUsernameLocksIdenticallySoLockoutIsNotAnEnumerationOracle()
    {
        var (controller, _) = Controller();
        for (var i = 0; i < LoginAttemptGuard.MaxFailures; i++)
        {
            AssertStatus(HttpStatusCode.Unauthorized, controller.Login(new("no-such-user", "x")));
        }
        var locked = controller.Login(new("no-such-user", "x"));
        AssertStatus(HttpStatusCode.TooManyRequests, locked);
        var body = (AuthErrorResponse)((ObjectResult)locked.Result!).Value!;
        Assert.AreEqual("login_temporarily_locked", body.Code);
    }

    [TestMethod]
    public void StagingBootstrapAccountIsAlsoProtected()
    {
        var (controller, _) = Controller("Production");
        for (var i = 0; i < LoginAttemptGuard.MaxFailures; i++)
        {
            AssertStatus(HttpStatusCode.Unauthorized, controller.Login(new(StagingUser, "guess-" + i)));
        }
        AssertStatus(HttpStatusCode.TooManyRequests, controller.Login(new(StagingUser, StagingPassword)));
    }

    // ---- malformed input ------------------------------------------------

    [TestMethod]
    [DataRow(null, "password123")]
    [DataRow("operator1", null)]
    [DataRow("", "password123")]
    [DataRow("   ", "password123")]
    [DataRow("operator1", "")]
    public void MalformedCredentialsGetGenericRejection(string? username, string? password)
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            var (controller, _) = Controller(environment);
            var result = controller.Login(new(username!, password!));
            AssertStatus(HttpStatusCode.Unauthorized, result);
            Assert.AreEqual("invalid_credentials",
                ((AuthErrorResponse)((ObjectResult)result.Result!).Value!).Code);
        }
    }

    [TestMethod]
    public void NullBodyAndOversizedCredentialsAreRejected()
    {
        var (controller, _) = Controller("Production");
        AssertStatus(HttpStatusCode.Unauthorized, controller.Login(null));
        var big = new string('a', LoginAttemptGuard.MaxCredentialLength + 1);
        AssertStatus(HttpStatusCode.Unauthorized, controller.Login(new(big, StagingPassword)));
        AssertStatus(HttpStatusCode.Unauthorized, controller.Login(new(StagingUser, big)));
        Assert.IsTrue(LoginAttemptGuard.IsWellFormed(
            new string('a', LoginAttemptGuard.MaxCredentialLength), "p"));
    }

    [TestMethod]
    public void MalformedAttemptsDoNotCountTowardLockout()
    {
        var (controller, _) = Controller();
        var big = new string('p', LoginAttemptGuard.MaxCredentialLength + 1);
        for (var i = 0; i < LoginAttemptGuard.MaxFailures * 2; i++)
        {
            controller.Login(new("operator1", big));
        }
        AssertStatus(HttpStatusCode.OK, controller.Login(new("operator1", "password123")));
    }

    // ---- dev identities outside Development ----------------------------

    [TestMethod]
    [DataRow("operator1")]
    [DataRow("reviewer1")]
    [DataRow("publisher1")]
    [DataRow("admin1")]
    [DataRow(" Reviewer1 ")]
    public void EveryDevFixtureIdentityIsRefusedAsBootstrapOutsideDevelopment(string username)
    {
        foreach (var environment in new[] { "Production", "Staging" })
        {
            var configuration = Config(new Dictionary<string, string?>
            {
                ["Auth:Bootstrap:Username"] = username,
                ["Auth:Bootstrap:Password"] = StagingPassword
            });
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                ProductionBootstrapAuthenticator.ValidateConfiguration(configuration, environment));
        }
    }

    [TestMethod]
    public void DevFixtureLoginIsRefusedOutsideDevelopment()
    {
        var (controller, _) = Controller("Production");
        AssertStatus(HttpStatusCode.Unauthorized, controller.Login(new("admin1", "password123")));
        AssertStatus(HttpStatusCode.OK, controller.Login(new(StagingUser, StagingPassword)));
    }

    // ---- helpers --------------------------------------------------------

    private static (AuthController Controller, ManualClock Clock) Controller(
        string environment = "Development")
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Auth:Bootstrap:Username"] = StagingUser,
            ["Auth:Bootstrap:Password"] = StagingPassword,
            ["Auth:Jwt:Secret"] = environment == "Development"
                ? null
                : "a-staging-only-jwt-signing-secret-of-sufficient-length-0123456789"
        });
        var host = new TestHostEnvironment(environment);
        var clock = new ManualClock();
        var controller = new AuthController(
            new DevUserStore(),
            new ProductionBootstrapAuthenticator(configuration),
            new JwtTokenService(configuration, host),
            new AuthRequestContext(),
            host,
            new LoginAttemptGuard(clock))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, clock);
    }

    private static int StatusOf(ActionResult<LoginResponse> result) =>
        result.Result is IStatusCodeActionResult status
            ? status.StatusCode ?? StatusCodes.Status200OK
            : StatusCodes.Status200OK;

    private static void AssertStatus(HttpStatusCode expected, ActionResult<LoginResponse> result)
    {
        var actual = StatusOf(result);
        Assert.AreEqual((int)expected, actual);
        if (expected == HttpStatusCode.OK)
        {
            Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        }
    }

    private static DefaultHttpContext Client(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    private static IConfiguration Config(IDictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "VeritasAtlas.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
