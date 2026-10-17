namespace Firmly.Ipc.Tests;

public class BiosPasswordGateTests
{
    private const string Password = "s3cret";

    private static readonly FirmlyServiceException Refused = new(IpcError.BiosPasswordRequired, "missing or wrong");

    [Fact]
    public async Task NoPassword_SendsNone_AndAsksNothing()
    {
        var source = new ScriptedSource();
        var gate = new BiosPasswordGate { Source = source };

        string? sent = "unset";
        var result = await gate.CallAsync(password => { sent = password; return Task.FromResult(1); });

        Assert.Equal(1, result);
        Assert.Null(sent);
        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task PasswordSet_AsksFirst_SendsIt_AndReportsItAccepted()
    {
        var source = new ScriptedSource(new BiosPasswordAnswer(Password, Remember: true));
        var gate = new BiosPasswordGate { Source = source, IsSet = true };

        var sent = new List<string?>();
        await gate.CallAsync(password => { sent.Add(password); return Task.FromResult(0); });

        Assert.Equal([Password], sent);
        Assert.Equal([null], source.Asked);
        Assert.Equal(Password, source.Accepted?.Password);
    }

    [Fact]
    public async Task Refused_AsksAgain_WithTheRefusedAnswer()
    {
        var wrong = new BiosPasswordAnswer("wrong", FromVault: true);
        var source = new ScriptedSource(wrong, new BiosPasswordAnswer(Password));
        var gate = new BiosPasswordGate { Source = source, IsSet = true };

        await gate.CallAsync(password => password == Password ? Task.FromResult(0) : throw Refused);

        Assert.Equal([null, wrong], source.Asked);
        Assert.Equal(Password, source.Accepted?.Password);
    }

    [Fact]
    public async Task PasswordFirmlyDidntKnowOf_IsAskedFor_OnceRefused()
    {
        var source = new ScriptedSource(new BiosPasswordAnswer(Password));
        var gate = new BiosPasswordGate { Source = source };

        await gate.CallAsync(password => password == Password ? Task.FromResult(0) : throw Refused);

        Assert.True(gate.IsSet);
        Assert.Equal([null], source.Asked);
    }

    [Fact]
    public async Task Cancelled_ChangesNothing()
    {
        var gate = new BiosPasswordGate { Source = new ScriptedSource(), IsSet = true };
        var called = false;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => gate.CallAsync(_ => { called = true; return Task.FromResult(0); }));
        Assert.False(called);
    }

    [Fact]
    public async Task RefusedThreeTimes_GivesUp()
    {
        var source = new ScriptedSource(new("a"), new("b"), new("c"), new("d"));
        var gate = new BiosPasswordGate { Source = source, IsSet = true };
        var tries = 0;

        var ex = await Assert.ThrowsAsync<FirmlyServiceException>(
            () => gate.CallAsync<int>(_ => { tries++; throw Refused; }));
        Assert.Equal(IpcError.BiosPasswordRequired, ex.Error);
        Assert.Equal(3, tries);
        Assert.Null(source.Accepted);
    }

    [Fact]
    public async Task OtherErrors_AreNotRetried()
    {
        var source = new ScriptedSource(new BiosPasswordAnswer(Password));
        var gate = new BiosPasswordGate { Source = source, IsSet = true };

        await Assert.ThrowsAsync<FirmlyServiceException>(() => gate.CallAsync<int>(
            _ => throw new FirmlyServiceException(IpcError.NotAllowed, "no")));
        Assert.Single(source.Asked);
    }

    [Fact]
    public void Answer_NeverPrintsThePassword() =>
        Assert.DoesNotContain(Password, new BiosPasswordAnswer(Password).ToString());

    // Gives the answers in order, then cancels; remembers what it was asked and what was accepted.
    private sealed class ScriptedSource(params BiosPasswordAnswer[] answers) : IBiosPasswordSource
    {
        private readonly Queue<BiosPasswordAnswer> _answers = new(answers);

        public List<BiosPasswordAnswer?> Asked { get; } = [];

        public BiosPasswordAnswer? Accepted { get; private set; }

        public Task<BiosPasswordAnswer?> GetAsync(BiosPasswordAnswer? rejected)
        {
            Asked.Add(rejected);
            return Task.FromResult(_answers.TryDequeue(out var answer) ? answer : null);
        }

        public Task AcceptedAsync(BiosPasswordAnswer answer)
        {
            Accepted = answer;
            return Task.CompletedTask;
        }
    }
}
