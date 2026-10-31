using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.Users;

// The pre-provisioned users of Users:Pool. A test leases one (or more) for its duration and returns it; a user is
// never held by two tests at once. A lease waits for a matching user in the order the leases came, up to the lease
// timeout; a role nobody in the pool has fails at once.
public sealed class UserPool
{
    private readonly object gate = new();
    private readonly List<TestUser> free;
    private readonly HashSet<TestUser> leased = [];
    private readonly LinkedList<Waiter> waiters = new();
    private readonly TimeProvider timeProvider;

    public UserPool(IEnumerable<TestUser> users, TimeSpan leaseTimeout, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseTimeout, TimeSpan.Zero);

        Users = users.ToArray();
        free = [.. Users];
        LeaseTimeout = leaseTimeout;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<TestUser> Users { get; }

    public TimeSpan LeaseTimeout { get; }

    public int LeasedCount
    {
        get
        {
            lock (gate)
            {
                return leased.Count;
            }
        }
    }

    public static UserPool FromSettings(UsersSettings settings, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new UserPool(TestUser.FromSettings(settings), TimeSpan.FromSeconds(settings.LeaseTimeoutSeconds), timeProvider);
    }

    // Throws UserLeaseException when no user of the pool satisfies the requirement, or none became free in time.
    public async Task<TestUser> LeaseAsync(UserRequirement requirement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        cancellationToken.ThrowIfCancellationRequested();

        Waiter waiter;
        lock (gate)
        {
            if (!Users.Any(requirement.IsSatisfiedBy))
            {
                throw new UserLeaseException(Users.Count == 0
                    ? "Users:Pool is empty; the test needs a user."
                    : $"No user of Users:Pool has {requirement}; the pool has {Users.Count}.");
            }

            var user = free.FirstOrDefault(requirement.IsSatisfiedBy);
            if (user is not null)
            {
                free.Remove(user);
                leased.Add(user);
                return user;
            }

            waiter = new Waiter(requirement);
            waiter.Node = waiters.AddLast(waiter);
        }

        using var timeout = new CancellationTokenSource(LeaseTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        await using var registration = linked.Token.Register(() => Abandon(waiter));

        try
        {
            return await waiter.Completion.Task;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            int leasedNow;
            lock (gate)
            {
                leasedNow = leased.Count;
            }

            throw new UserLeaseException(
                $"No free user with {requirement} within {LeaseTimeout.TotalSeconds:0} s; the pool has {Users.Count}, {leasedNow} leased.");
        }
    }

    // Hands the user to the first waiting lease it satisfies, or back to the free users; returning twice is harmless.
    public void Return(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (gate)
        {
            if (!leased.Remove(user))
            {
                return;
            }

            for (var node = waiters.First; node is not null; node = node.Next)
            {
                if (node.Value.Requirement.IsSatisfiedBy(user))
                {
                    waiters.Remove(node);
                    node.Value.Node = null;
                    leased.Add(user);
                    node.Value.Completion.TrySetResult(user);
                    return;
                }
            }

            var index = free.FindIndex(other => other.Index > user.Index);
            free.Insert(index < 0 ? free.Count : index, user);
        }
    }

    private void Abandon(Waiter waiter)
    {
        lock (gate)
        {
            if (waiter.Node is null)
            {
                // Already handed a user; the lease completes with it.
                return;
            }

            waiters.Remove(waiter.Node);
            waiter.Node = null;
        }

        waiter.Completion.TrySetCanceled();
    }

    private sealed class Waiter(UserRequirement requirement)
    {
        public UserRequirement Requirement { get; } = requirement;

        public TaskCompletionSource<TestUser> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LinkedListNode<Waiter>? Node { get; set; }
    }
}
