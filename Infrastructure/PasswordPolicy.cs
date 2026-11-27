using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace UFSWebApp.Infrastructure
{
    public static class PasswordPolicy
    {
        public const int MinimumLength = 8;

        public const string RulesText =
            "At least 8 characters with an uppercase letter, a lowercase letter, a number and a symbol. " +
            "Common passwords (e.g. Password123, P@ssw0rd, Qwerty1!) and your own name, email or student/staff number are not allowed.";

        private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
        {
            "123456","1234567","12345678","123456789","1234567890","12345","1234","111111","000000","123123",
            "654321","666666","121212","112233","987654321","11111111","88888888","12341234","147258369","159753",
            "password","passw0rd","pass","passwd","password1","password12","password123","mypassword","letmein","welcome",
            "welcome1","qwerty","qwertyuiop","qwerty123","asdf","asdfgh","asdfghjkl","zxcvbn","zxcvbnm","azerty",
            "abc","abcd","abc123","abcd1234","abcdef","abcdefg","abcdefgh","aa123456","a123456","q1w2e3r4",
            "1q2w3e4r","1qaz2wsx","qazwsx","zaq12wsx","iloveyou","iloveu","loveyou","love","lovely","princess",
            "admin","administrator","root","user","guest","login","test","test123","demo","master",
            "secret","changeme","default","access","hello","hello123","sunshine","shadow","monkey","dragon",
            "football","soccer","baseball","basketball","superman","batman","starwars","pokemon","naruto","trustno1",
            "freedom","whatever","computer","internet","samsung","iphone","google","facebook","michael","jennifer",
            "jordan","charlie","daniel","thomas","jesus","god","blessed","blessing","faith","grace",
            "summer","winter","spring","autumn","january","february","december","monday","friday","sunday",
            "flower","cookie","chocolate","cheese","banana","orange","apple","pepper","ginger","tiger",
            "killer","hunter","ranger","buster","maggie","ginger","purple","yellow","silver","golden",
            "mustang","ferrari","porsche","mercedes","toyota","bmw","hondacivic","harley","chelsea","arsenal",
            "liverpool","manutd","kaizerchiefs","orlandopirates","sundowns","bafana","springbok","southafrica","mzansi","africa",
            "ufs","ufsqwaqwa","qwaqwa","kovsies","student","students","university","campus","estate","staff",
            "bank","estatebank","money","payment","rent","residence","agent","finance","officer","customer"
        };

        private static string Normalise(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s.ToLowerInvariant())
            {
                sb.Append(ch switch
                {
                    '@' or '4' => 'a',
                    '0' => 'o',
                    '1' or '!' or '|' => 'i',
                    '3' => 'e',
                    '$' or '5' => 's',
                    '7' or '+' => 't',
                    _ => char.IsLetterOrDigit(ch) ? ch : '\0'
                });
            }
            return sb.ToString().Replace("\0", "");
        }

        private static bool IsCommon(string password)
        {
            var lower = new string(password.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (Common.Contains(lower)) return true;

            var core = lower.Trim("0123456789".ToCharArray());
            if (core.Length > 0 && Common.Contains(core)) return true;

            var leet = Normalise(password).Trim("0123456789".ToCharArray());
            if (leet.Length > 0 && Common.Contains(leet)) return true;
            if (leet.Length > 1 && Common.Contains(leet.TrimEnd('i'))) return true;

            if (lower.Distinct().Count() <= 2) return true;
            foreach (var run in new[] { "0123456789", "abcdefghijklmnopqrstuvwxyz", "qwertyuiop", "asdfghjkl", "zxcvbnm" })
                if (lower.Length >= 6 && run.Contains(lower)) return true;

            return false;
        }

        public static List<string> Validate(string? password, params string?[] personalInfo)
        {
            var errors = new List<string>();
            password ??= "";

            if (password.Length < MinimumLength) errors.Add($"Password must be at least {MinimumLength} characters long.");
            if (!password.Any(char.IsUpper)) errors.Add("Password must contain at least one uppercase letter.");
            if (!password.Any(char.IsLower)) errors.Add("Password must contain at least one lowercase letter.");
            if (!password.Any(char.IsDigit)) errors.Add("Password must contain at least one number.");
            if (password.All(char.IsLetterOrDigit)) errors.Add("Password must contain at least one symbol (e.g. ! @ # $ %).");
            if (password.Any(char.IsWhiteSpace)) errors.Add("Password must not contain spaces.");

            if (password.Length > 0 && IsCommon(password))
                errors.Add("This password is too common and easy to guess. Please choose a less predictable password.");

            var lowerPwd = password.ToLowerInvariant();
            foreach (var info in personalInfo)
            {
                if (string.IsNullOrWhiteSpace(info)) continue;
                var piece = info.Contains('@') ? info[..info.IndexOf('@')] : info;
                piece = piece.Trim().ToLowerInvariant();
                if (piece.Length >= 3 && lowerPwd.Contains(piece))
                {
                    errors.Add("Password must not contain your name, surname, email or student/staff number.");
                    break;
                }
            }

            return errors;
        }

        public static bool AddErrors(ModelStateDictionary modelState, string key, string? password, params string?[] personalInfo)
        {
            var errors = Validate(password, personalInfo);
            foreach (var e in errors) modelState.AddModelError(key, e);
            return errors.Count == 0;
        }
    }
}
