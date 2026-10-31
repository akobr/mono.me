using System.Collections.Concurrent;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Extensions.Time.Testing;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Users;

public sealed class UserPoolTests
{
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task LeaseAsync_HandsOutFreeUsersInPoolOrder()
    {
        var pool = Pool(("owner@example.test", ["Owner"]), ("reader@example.test", ["Reader"]));

        var first = await pool.LeaseAsync(UserRequirement.Any);
        var second = await pool.LeaseAsync(UserRequirement.Any);

        first.Email.ShouldBe("owner@example.test");
        second.Email.ShouldBe("reader@example.test");
        pool.LeasedCount.ShouldBe(2);
    }

    [Fact]
    public async Task LeaseAsync_FiltersByRole()
    {
        var pool = Pool(("owner@example.test", ["Owner"]), ("contributor@example.test", ["Contributor"]), ("reader@example.test", ["Reader"]));

        var reader = await pool.LeaseAsync(UserRequirement.WithRole("reader"));
        var either = await pool.LeaseAsync(UserRequirement.WithAnyRole("Contributor", "Owner"));

        reader.Email.ShouldBe("reader@example.test");
        either.Email.ShouldBe("owner@example.test");
    }

    [Fact]
    public async Task LeaseAsync_FailsAtOnce_ForARoleNobodyHas()
    {
        var pool = Pool(("owner@example.test", ["Owner"]), ("reader@example.test", ["Reader"]));

        var exception = await Should.ThrowAsync<UserLeaseException>(() => pool.LeaseAsync(UserRequirement.WithRole("Administrator")));

        exception.Message.ShouldBe("No user of Users:Pool has role Administrator; the pool has 2.");
    }

    [Fact]
    public async Task LeaseAsync_FailsForAnEmptyPool()
    {
        var pool = new UserPool([], LeaseTimeout);

        var exception = await Should.ThrowAsync<UserLeaseException>(() => pool.LeaseAsync(UserRequirement.Any));

        exception.Message.ShouldBe("Users:Pool is empty; the test needs a user.");
    }

    [Fact]
    public async Task LeaseAsync_TimesOut_WithTheStateOfThePool()
    {
        var time = new FakeTimeProvider(TestData.Start);
        var pool = Pool(time, ("owner@example.test", ["Owner"]), ("reader@example.test", ["Reader"]));
        await pool.LeaseAsync(UserRequirement.WithRole("Owner"));

        var waiting = pool.LeaseAsync(UserRequirement.WithRole("Owner"));
        time.Advance(LeaseTimeout - TimeSpan.FromSeconds(1));
        await Eventually.SettleAsync();
        waiting.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(1));

        var exception = await Should.ThrowAsync<UserLeaseException>(() => waiting);
        exception.Message.ShouldBe("No free user with role Owner within 120 s; the pool has 2, 1 leased.");
    }

    [Fact]
    public async Task Return_HandsTheUserToTheFirstMatchingWaiter()
    {
        var pool = Pool(("owner@example.test", ["Owner"]), ("reader@example.test", ["Reader"]));
        var owner = await pool.LeaseAsync(UserRequirement.WithRole("Owner"));
        var reader = await pool.LeaseAsync(UserRequirement.WithRole("Reader"));

        var waitsForOwner = pool.LeaseAsync(UserRequirement.WithRole("Owner"));
        var waitsForAny = pool.LeaseAsync(UserRequirement.Any);

        pool.Return(reader);
        (await waitsForAny).ShouldBeSameAs(reader);
        waitsForOwner.IsCompleted.ShouldBeFalse();

        pool.Return(owner);
        (await waitsForOwner).ShouldBeSameAs(owner);
        pool.LeasedCount.ShouldBe(2);
    }

    [Fact]
    public async Task LeaseAsync_ServesWaitersInOrder()
    {
        var pool = Pool(("only@example.test", []));
        var user = await pool.LeaseAsync(UserRequirement.Any);

        var first = pool.LeaseAsync(UserRequirement.Any);
        var second = pool.LeaseAsync(UserRequirement.Any);

        pool.Return(user);
        (await first).ShouldBeSameAs(user);
        second.IsCompleted.ShouldBeFalse();

        pool.Return(user);
        (await second).ShouldBeSameAs(user);
    }

    [Fact]
    public async Task LeaseAsync_CanBeCancelled_AndLeavesNoWaiterBehind()
    {
        var pool = Pool(("only@example.test", []));
        var user = await pool.LeaseAsync(UserRequirement.Any);
        using var cancellation = new CancellationTokenSource();

        var waiting = pool.LeaseAsync(UserRequirement.Any, cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
        pool.Return(user);
        pool.LeasedCount.ShouldBe(0);
        (await pool.LeaseAsync(UserRequirement.Any)).ShouldBeSameAs(user);
    }

    [Fact]
    public async Task Return_Twice_IsHarmless()
    {
        var pool = Pool(("only@example.test", []));
        var user = await pool.LeaseAsync(UserRequirement.Any);

        pool.Return(user);
        pool.Return(user);

        pool.LeasedCount.ShouldBe(0);
        (await pool.LeaseAsync(UserRequirement.Any)).ShouldBeSameAs(user);
        pool.LeasedCount.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentLeases_NeverShareAUser()
    {
        var pool = Pool(("a@example.test", ["Owner"]), ("b@example.test", ["Owner"]), ("c@example.test", ["Reader"]), ("d@example.test", ["Reader"]));
        var holders = new ConcurrentDictionary<TestUser, int>();
        var maximum = 0;
        var current = 0;

        async Task RunTestAsync(int number)
        {
            var requirement = number % 3 == 0 ? UserRequirement.WithRole("Owner") : UserRequirement.Any;
            var user = await pool.LeaseAsync(requirement);
            try
            {
                holders.TryAdd(user, number).ShouldBeTrue($"{user} is held by test {holders.GetValueOrDefault(user)} and test {number}.");
                var now = Interlocked.Increment(ref current);
                InterlockedMax(ref maximum, now);
                await Task.Delay(Random.Shared.Next(0, 3));
                if (requirement.Roles.Count > 0)
                {
                    user.HasRole("Owner").ShouldBeTrue();
                }
            }
            finally
            {
                Interlocked.Decrement(ref current);
                holders.TryRemove(user, out _).ShouldBeTrue();
                pool.Return(user);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 200).Select(number => Task.Run(() => RunTestAsync(number))));

        maximum.ShouldBeLessThanOrEqualTo(4);
        pool.LeasedCount.ShouldBe(0);
    }

    [Fact]
    public void TestUser_ShowsAStableFingerprint_NeverTheAddressOrPassword()
    {
        var user = new TestUser(3, "UI-01@example.test", "pw-123");
        var same = new TestUser(0, "ui-01@example.test");

        user.Fingerprint.ShouldBe(same.Fingerprint);
        user.Fingerprint.ShouldMatch("^u-[0-9a-f]{8}$");
        user.ToString().ShouldBe($"Users:Pool:3 ({user.Fingerprint})");
        user.ToString().ShouldNotContain("example");
    }

    [Fact]
    public void FromSettings_KeepsThePoolOrderAndTheRoles()
    {
        var settings = new UsersSettings
        {
            LeaseTimeoutSeconds = 30,
            Pool = [new PoolUserSettings { Email = "a@example.test", Roles = ["Owner"], DisplayName = "Ada" }, new PoolUserSettings { Email = "b@example.test" }],
        };

        var pool = UserPool.FromSettings(settings);

        pool.LeaseTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        pool.Users.Select(user => user.Index).ShouldBe([0, 1]);
        pool.Users[0].DisplayName.ShouldBe("Ada");
        pool.Users[0].HasRole("owner").ShouldBeTrue();
    }

    private static UserPool Pool(params (string Email, string[] Roles)[] users)
    {
        return Pool(TimeProvider.System, users);
    }

    private static UserPool Pool(TimeProvider time, params (string Email, string[] Roles)[] users)
    {
        return new UserPool(users.Select((user, index) => new TestUser(index, user.Email, roles: user.Roles)), LeaseTimeout, time);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}
