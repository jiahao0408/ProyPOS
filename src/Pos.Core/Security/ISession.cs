using Pos.Core.Domain;

namespace Pos.Core.Security;

/// <summary>Usuario con la sesión abierta en este puesto.</summary>
public interface ISession
{
    User? CurrentUser { get; }

    bool IsAdmin => CurrentUser?.IsAdmin == true;

    void SignIn(User user);

    void SignOut();

    event EventHandler? Changed;
}

public sealed class Session : ISession
{
    public User? CurrentUser { get; private set; }

    public event EventHandler? Changed;

    public void SignIn(User user)
    {
        CurrentUser = user;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUser = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
