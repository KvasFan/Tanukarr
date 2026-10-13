using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.MDBList
{
    public class MDBListSettingsValidator : AbstractValidator<MDBListSettings>
    {
        public MDBListSettingsValidator()
        {
            RuleFor(s => s.ApiKey).NotEmpty();
            RuleFor(s => s.IncludedListUrls).Must(values => ImportListSetOperations.Sources(values).Length > 0).WithMessage("Select at least one included MDBList URL.");
            RuleForEach(s => s.IncludedListUrls).Must(url => MDBListSettings.GetListPath(url) != null).WithMessage("Paste an MDBList URL such as https://mdblist.com/lists/username/list-name");
            RuleForEach(s => s.ExcludedListUrls).Must(url => MDBListSettings.GetListPath(url) != null).WithMessage("Paste an MDBList URL such as https://mdblist.com/lists/username/list-name");
        }
    }

    public class MDBListSettings : ImportListSettingsBase<MDBListSettings>, IJsonOnDeserialized
    {
        private static readonly MDBListSettingsValidator Validator = new();

        public override string BaseUrl { get; set; } = "https://api.mdblist.com";

        [FieldDefinition(0, Label = "MDBListListUrl", Hidden = HiddenType.Hidden)]
        public string ListUrl { get; set; } = string.Empty;

        [FieldDefinition(2, Label = "ImportListsIncludedLists", HelpText = "MDBListIncludedListsHelp", Type = FieldType.Tag)]
        public IEnumerable<string> IncludedListUrls { get; set; } = Array.Empty<string>();

        [FieldDefinition(3, Label = "ImportListsExcludedLists", HelpText = "MDBListExcludedListsHelp", Type = FieldType.Tag)]
        public IEnumerable<string> ExcludedListUrls { get; set; } = Array.Empty<string>();

        [FieldDefinition(1, Label = "ApiKey", HelpText = "MDBListApiKeyHelp", Privacy = PrivacyLevel.ApiKey)]
        public string ApiKey { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            NormalizeLegacy();
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        public static string GetListPath(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
                uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
                (uri.Host != "mdblist.com" && uri.Host != "www.mdblist.com"))
            {
                return null;
            }

            var match = Regex.Match(uri.AbsolutePath, @"^/lists/([a-zA-Z0-9_-]+)/([a-zA-Z0-9_-]+)/?$", RegexOptions.None, TimeSpan.FromSeconds(1));
            return match.Success ? $"lists/{match.Groups[1].Value}/{match.Groups[2].Value}/items/show" : null;
        }

        void IJsonOnDeserialized.OnDeserialized() => NormalizeLegacy();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => NormalizeLegacy();

        private void NormalizeLegacy()
        {
            IncludedListUrls = ImportListSetOperations.Sources(IncludedListUrls, ListUrl);
            ListUrl = string.Empty;
        }
    }
}
