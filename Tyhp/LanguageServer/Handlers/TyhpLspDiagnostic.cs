namespace Tyhp.LanguageServer.Handlers
{
    using System.Runtime.Serialization;
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Newtonsoft.Json;

    /// <summary>
    /// Extends the 17.2.8 <see cref="Diagnostic"/> DTO with LSP
    /// <c>relatedInformation</c>, which that package does not declare.
    /// </summary>
    /// <remarks>
    /// <see cref="Diagnostic"/> is a <see cref="DataContractAttribute"/> type whose
    /// members are opted into serialization individually via <see cref="DataMemberAttribute"/>
    /// / <see cref="JsonPropertyAttribute"/>. A plain auto-property on a derived class is
    /// silently dropped by <c>Newtonsoft.Json</c>'s data-contract resolver — both attributes
    /// are required so IDEs receive click-through locations for secondary diagnostic spans.
    /// </remarks>
    [DataContract]
    internal sealed class TyhpLspDiagnostic : Diagnostic
    {
        /// <summary>
        /// Related locations (Tyhp diagnostic labels), serialized as LSP
        /// <c>relatedInformation</c>.
        /// </summary>
        [DataMember(Name = "relatedInformation")]
        [JsonProperty("relatedInformation", NullValueHandling = NullValueHandling.Ignore)]
        public DiagnosticRelatedInformation[]? RelatedInformation { get; set; }
    }

    /// <summary>
    /// One LSP <c>DiagnosticRelatedInformation</c> item. The 17.2.8 protocol package
    /// does not ship this type.
    /// </summary>
    [DataContract]
    internal sealed class DiagnosticRelatedInformation
    {
        /// <summary>Location the related message refers to.</summary>
        [DataMember(Name = "location")]
        [JsonProperty("location")]
        public Location Location { get; set; } = new();

        /// <summary>Short label shown next to the location (e.g. "declared here").</summary>
        [DataMember(Name = "message")]
        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }
}
