namespace GogDisc.Packager;

/// <summary>
/// Raised when the user closes the sign-in dialog instead of replacing a credential GOG rejected.
/// Declining is a choice, not a failure, so callers report it quietly rather than as an error.
/// </summary>
public sealed class SignInDeclinedException : Exception
{
    public SignInDeclinedException() : base("GOG sign-in was declined.") { }
}
