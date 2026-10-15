using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Talynor.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateMapRequest([property: JsonRequired] string Title, string? Description);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateMapRequest(string? Title, string? Description, bool ClearDescription = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PositionRequest([property: JsonRequired] double X, [property: JsonRequired] double Y);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateNodeRequest([property: JsonRequired] string Title, string? Description,
    [property: JsonRequired] [Required] PositionRequest Position, Guid? ChildMapId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateNodeRequest(string? Title, string? Description, bool ClearDescription = false,
    PositionRequest? Position = null, Guid? ChildMapId = null, bool ClearChildMap = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateEdgeRequest([property: JsonRequired] Guid SourceNodeId, [property: JsonRequired] Guid TargetNodeId);
