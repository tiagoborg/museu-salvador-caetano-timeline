using System;
using System.Security.Cryptography;

namespace EnigmaVirtual
{
    /// <summary>
    /// Hashing and verification for the settings-panel password.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PBKDF2-HMAC-SHA1 with a per-password random salt, stored as one self-describing string:
    /// <c>pbkdf2-sha1$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>. One string means one
    /// settings field, so no salt or iteration count has to be configured separately, and an old build
    /// reading a newer hash fails closed rather than half-parsing it.
    /// </para>
    /// <para>
    /// <b>SHA-1 is deliberate.</b> The <c>Rfc2898DeriveBytes</c> overload that takes a
    /// <c>HashAlgorithmName</c> needs .NET Framework 4.7.2, which is not safe to assume across Unity's
    /// Mono versions; the three-argument constructor is universally available and gives PBKDF2-HMAC-SHA1.
    /// PBKDF2-HMAC-SHA1 is not broken for password hashing - SHA-1's collision weaknesses do not apply to
    /// HMAC key stretching - and the iteration count is what does the work here.
    /// </para>
    /// <para>
    /// <b>Do not use <see cref="StringUtil.CalculateMD5Hash"/> for this.</b> MD5 is unsuitable for
    /// passwords regardless of how it is called - and it is now <c>[Obsolete]</c> partly so this warning
    /// reaches a call site rather than living only here. Its undisposed handle and ASCII mangling were
    /// fixed in v0.33.0; its unsuitability for credentials was never about those.
    /// </para>
    /// <para>
    /// <b>What this does and does not buy.</b> It stops the password being readable at rest - which is the
    /// actual defect, a plaintext <c>"gema"</c> committed to a template that every client project is
    /// branched from, where a reused password leaks to anyone who reads a repo. It does <b>not</b> protect
    /// the panel from someone who can write <c>settings.json</c>, because they can simply replace the hash.
    /// That is not a gap to close here: on a kiosk, file-write access is game over already.
    /// </para>
    /// </remarks>
    public static class PanelPassword
    {
        private const string Algorithm = "pbkdf2-sha1";
        private const char FieldSeparator = '$';

        /// <summary>
        /// Iterations used for a newly generated hash.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Low by password-hashing standards, and chosen from measurement rather than folklore.
        /// Verification is synchronous inside a UI click handler, so its cost is a frozen frame. On the
        /// machine this was written on: 10,000 iterations is 57&#160;ms, 100,000 is 547&#160;ms.
        /// Half a second of dead UI on a kiosk reads as "the button did not work", and the operator
        /// clicks again - which now spends another attempt against the lockout. 57&#160;ms is invisible.
        /// </para>
        /// <para>
        /// The usual 600k+ advice is for an internet-facing credential store, where an attacker has the
        /// hashes and not the machine. Here the threat is disclosure: a plaintext
        /// password committed to a template that every client project branches from. A salt plus any real
        /// iteration count defeats reading it and defeats rainbow tables, and beyond that the ceiling is
        /// the password's own entropy - against a five-letter word, 10,000 and 600,000 are the same answer.
        /// </para>
        /// <para>
        /// A site that wants more can raise it: the count travels inside the stored hash, so a hash
        /// generated at a higher count verifies at that count with no code change. <see cref="MaxIterations"/>
        /// is what bounds the resulting stall.
        /// </para>
        /// </remarks>
        private const int DefaultIterations = 10000;

        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        /// <summary>
        /// <c>Rfc2898DeriveBytes</c> rejects a shorter salt with an exception, and verification runs in a
        /// UI click handler, so this is checked rather than caught.
        /// </summary>
        private const int MinSaltBytes = 8;

        /// <summary>
        /// PBKDF2-HMAC cost scales with salt size as well as iterations, so an unbounded salt is a second
        /// way to freeze the UI thread from a hand-edited config - a multi-megabyte base64 salt would do
        /// it at any iteration count. <see cref="MaxIterations"/> alone was not enough.
        /// </summary>
        private const int MaxSaltBytes = 64;

        /// <summary>Ceiling on derived bytes, for the same reason as <see cref="MaxIterations"/>.</summary>
        private const int MaxHashBytes = 64;

        /// <summary>
        /// Highest iteration count this will honour from a stored hash. <c>settings.json</c> is
        /// operator-editable, so an absurd count there would otherwise stall the UI thread on every
        /// login attempt. 200,000 measured at roughly 1.1&#160;s on the machine this was written on -
        /// deliberately a bad-but-survivable ceiling rather than the 5&#160;s that a million would cost.
        /// </summary>
        private const int MaxIterations = 200000;

        /// <summary>
        /// Produces a storable hash of <paramref name="password"/>. Returns an empty string for an
        /// empty password, since an empty credential must not become a valid one.
        /// </summary>
        public static string Hash(string password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            byte[] salt = new byte[SaltBytes];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, DefaultIterations))
            {
                return string.Join(FieldSeparator.ToString(), new[]
                {
                    Algorithm,
                    DefaultIterations.ToString(),
                    Convert.ToBase64String(salt),
                    Convert.ToBase64String(pbkdf2.GetBytes(HashBytes))
                });
            }
        }

        /// <summary>
        /// True when <paramref name="input"/> matches <paramref name="storedHash"/>. Fails closed: an
        /// empty, malformed or unrecognised <paramref name="storedHash"/> returns false and reports why,
        /// rather than throwing into a button handler or - worse - letting anything through.
        /// </summary>
        public static bool Verify(string input, string storedHash)
        {
            if (input == null || string.IsNullOrEmpty(storedHash))
                return false;

            string[] parts = storedHash.Split(FieldSeparator);
            if (parts.Length != 4)
            {
                Development.LogError("the settings-panel password hash is malformed (expected 4 "
                    + $"{FieldSeparator}-separated fields, found {parts.Length}), so no password can be "
                    + "accepted. Regenerate it with Gema > Set Panel Password. (H14)");
                return false;
            }

            if (parts[0] != Algorithm)
            {
                Development.LogError($"the settings-panel password hash names algorithm '{parts[0]}', "
                    + $"which this build cannot compute - it understands '{Algorithm}'. No password can "
                    + "be accepted. Regenerate it with Gema > Set Panel Password. (H14)");
                return false;
            }

            int iterations;
            if (!int.TryParse(parts[1], out iterations) || iterations < 1)
            {
                Development.LogError($"the settings-panel password hash has a non-numeric or zero "
                    + "iteration count, so no password can be accepted. Regenerate it with "
                    + "Gema > Set Panel Password. (H14)");
                return false;
            }

            if (iterations > MaxIterations)
            {
                Development.LogError($"the settings-panel password hash asks for {iterations} "
                    + $"iterations, above the {MaxIterations} cap this build will spend on one login "
                    + "attempt. Refusing rather than stalling the UI. (H14)");
                return false;
            }

            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                // The report is the return value; the exception is the expected outcome of bad config,
                // not an exceptional event, so it is not logged as one.
                Development.LogError("the settings-panel password hash has a salt or hash field that is "
                    + "not valid base64, so no password can be accepted. Regenerate it with "
                    + "Gema > Set Panel Password. (H14)");
                return false;
            }

            // Rfc2898DeriveBytes THROWS on a salt shorter than 8 bytes, and this runs inside a button
            // handler, so a hand-edited settings.json would take out the click rather than fail the
            // login. Checked, not caught. Hash() always writes 16.
            if (salt.Length < MinSaltBytes || salt.Length > MaxSaltBytes)
            {
                Development.LogError($"the settings-panel password hash has a {salt.Length}-byte salt, "
                    + $"outside the {MinSaltBytes}-{MaxSaltBytes} range this build accepts. No password "
                    + "can be accepted. Regenerate it with Gema > Set Panel Password. (H14)");
                return false;
            }

            // The stored length decides how many bytes to derive, and the store is operator-editable,
            // so it needs a ceiling like the iteration count does.
            if (expected.Length == 0 || expected.Length > MaxHashBytes)
            {
                Development.LogError($"the settings-panel password hash is {expected.Length} bytes, "
                    + $"outside the 1-{MaxHashBytes} range this build will derive. No password can be "
                    + "accepted. Regenerate it with Gema > Set Panel Password. (H14)");
                return false;
            }

            using (var pbkdf2 = new Rfc2898DeriveBytes(input, salt, iterations))
                return FixedTimeEquals(pbkdf2.GetBytes(expected.Length), expected);
        }

        /// <summary>
        /// True when <paramref name="value"/> has the shape this class produces. Lets callers tell a
        /// hash from a legacy plaintext password without attempting a verification.
        /// </summary>
        public static bool LooksLikeHash(string value)
        {
            return !string.IsNullOrEmpty(value)
                && value.StartsWith(Algorithm + FieldSeparator, StringComparison.Ordinal);
        }

        /// <summary>
        /// Length-independent comparison, so the number of matching leading bytes is not observable in
        /// the time taken. Overkill against a physical keypad, but it is four lines and the alternative
        /// is a comparison that is wrong in a way nobody would ever notice.
        /// </summary>
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int differences = 0;
            for (int i = 0; i < a.Length; i++)
                differences |= a[i] ^ b[i];

            return differences == 0;
        }
    }
}
