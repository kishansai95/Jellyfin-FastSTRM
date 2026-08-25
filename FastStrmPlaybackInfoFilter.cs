using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Controller.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace FastSTRM
{
    public class FastStrmPlaybackInfoFilter : IAsyncActionFilter
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly ILogger<FastStrmPlaybackInfoFilter> _logger;

        public FastStrmPlaybackInfoFilter(
            ILibraryManager libraryManager,
            IMediaSourceManager mediaSourceManager,
            ILogger<FastStrmPlaybackInfoFilter> logger)
        {
            _libraryManager = libraryManager;
            _mediaSourceManager = mediaSourceManager;
            _logger = logger;
        }

        private static IEnumerable<string> ParseLanguages(string? configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return Array.Empty<string>();
            }

            return configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static MediaStream? PickByLanguage(List<MediaStream> streams, string? preferredLanguages)
        {
            foreach (var language in ParseLanguages(preferredLanguages))
            {
                var match = streams.FirstOrDefault(s => s.Language != null && s.Language.StartsWith(language, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var requestPath = context.HttpContext.Request.Path.Value;
            if (string.IsNullOrEmpty(requestPath) || !requestPath.EndsWith("/PlaybackInfo", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Extract ItemId from RouteData or Query
            if (!context.RouteData.Values.TryGetValue("itemId", out var itemIdObj) &&
                !context.RouteData.Values.TryGetValue("Id", out itemIdObj))
            {
                if (!context.HttpContext.Request.Query.TryGetValue("itemId", out var queryItemId) &&
                    !context.HttpContext.Request.Query.TryGetValue("Id", out queryItemId))
                {
                    await next();
                    return;
                }
                itemIdObj = queryItemId.ToString();
            }

            if (itemIdObj == null || !Guid.TryParse(itemIdObj.ToString(), out var itemId))
            {
                await next();
                return;
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item == null || !(item is MediaBrowser.Controller.Entities.Video video))
            {
                await next();
                return;
            }

            // Check if it's a STRM file
            if (string.IsNullOrEmpty(video.Path) || !video.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            _logger.LogInformation("FastSTRM intercepted PlaybackInfo request for {Path}", video.Path);

            string? directLink = null;
            try
            {
                var lines = await File.ReadAllLinesAsync(video.Path);
                directLink = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
                if (string.IsNullOrEmpty(directLink) || (!directLink.StartsWith("http://") && !directLink.StartsWith("https://")))
                {
                    _logger.LogWarning("FastSTRM could not find a valid http link in {Path}, falling back.", video.Path);
                    await next();
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FastSTRM error reading {Path}", video.Path);
                await next();
                return;
            }

            string? token = context.HttpContext.Request.Query["api_key"];
            if (string.IsNullOrEmpty(token)) token = context.HttpContext.Request.Query["ApiKey"];
            if (string.IsNullOrEmpty(token) && context.HttpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var authStr = authHeader.ToString();
                var tokenMatch = System.Text.RegularExpressions.Regex.Match(authStr, @"Token=""([^""]+)""");
                if (tokenMatch.Success)
                {
                    token = tokenMatch.Groups[1].Value;
                }
            }

            var mediaStreams = _mediaSourceManager.GetMediaStreams(new MediaStreamQuery
            {
                ItemId = video.Id
            }).ToList();

            foreach (var stream in mediaStreams)
            {
                if (stream.Type == MediaStreamType.Subtitle)
                {
                    if (stream.Codec == "subrip" || stream.Codec == "srt" || stream.Codec == "vtt" || stream.Codec == "ass" || stream.Codec == "ssa")
                    {
                        stream.SupportsExternalStream = true;
                    }

                    if (stream.IsExternal)
                    {
                        stream.DeliveryMethod = SubtitleDeliveryMethod.External;
                        string format = stream.IsTextSubtitleStream ? "vtt" : (stream.Codec ?? "vtt");
                        if (format == "subrip" || format == "srt") format = "vtt";
                        stream.DeliveryUrl = $"/Videos/{video.Id}/{video.Id:N}/Subtitles/{stream.Index}/0/Stream.{format}";
                        if (!string.IsNullOrEmpty(token))
                        {
                            stream.DeliveryUrl += $"?api_key={token}";
                        }
                    }
                }
            }

            int? requestedSubtitleIndex = null;
            int? requestedAudioIndex = null;

            if (context.HttpContext.Request.Query.TryGetValue("SubtitleStreamIndex", out var subIdxStr) && int.TryParse(subIdxStr, out int parsedSub))
                requestedSubtitleIndex = parsedSub;
            if (context.HttpContext.Request.Query.TryGetValue("AudioStreamIndex", out var audIdxStr) && int.TryParse(audIdxStr, out int parsedAud))
                requestedAudioIndex = parsedAud;

            foreach (var arg in context.ActionArguments.Values)
            {
                if (arg == null) continue;
                var type = arg.GetType();
                if (requestedSubtitleIndex == null)
                {
                    var subProp = type.GetProperty("SubtitleStreamIndex");
                    if (subProp != null)
                    {
                        var val = subProp.GetValue(arg);
                        if (val is int v) requestedSubtitleIndex = v;
                    }
                }
                if (requestedAudioIndex == null)
                {
                    var audProp = type.GetProperty("AudioStreamIndex");
                    if (audProp != null)
                    {
                        var val = audProp.GetValue(arg);
                        if (val is int v) requestedAudioIndex = v;
                    }
                }
            }

            var config = FastStrmPlugin.Instance?.Configuration;

            int? defaultAudioIndex = requestedAudioIndex;
            if (defaultAudioIndex == null)
            {
                var audioTracks = mediaStreams.Where(s => s.Type == MediaStreamType.Audio).ToList();
                var defaultAudio = PickByLanguage(audioTracks, config?.PreferredAudioLanguages)
                                   ?? audioTracks.FirstOrDefault(s => s.IsDefault)
                                   ?? audioTracks.FirstOrDefault();
                defaultAudioIndex = defaultAudio?.Index;
            }

            int? defaultSubtitleIndex = requestedSubtitleIndex;
            if (defaultSubtitleIndex == null)
            {
                var subtitles = mediaStreams.Where(s => s.Type == MediaStreamType.Subtitle).ToList();
                var defaultSubtitle = PickByLanguage(subtitles, config?.PreferredSubtitleLanguages)
                                      ?? subtitles.FirstOrDefault(s => s.IsDefault)
                                      ?? subtitles.FirstOrDefault(s => s.IsExternal)
                                      ?? subtitles.FirstOrDefault();
                defaultSubtitleIndex = defaultSubtitle?.Index;
            }

            var mediaSource = new MediaSourceInfo
            {
                Id = video.Id.ToString("N"),
                Path = directLink,
                Protocol = MediaBrowser.Model.MediaInfo.MediaProtocol.Http,
                Type = MediaSourceType.Default,
                IsRemote = true,
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                SupportsTranscoding = false,
                RunTimeTicks = video.RunTimeTicks,
                MediaStreams = mediaStreams,
                DefaultAudioStreamIndex = defaultAudioIndex,
                DefaultSubtitleStreamIndex = defaultSubtitleIndex,
                VideoType = VideoType.VideoFile,
                Container = "mp4", 
            };

            var playbackInfoResponse = new PlaybackInfoResponse
            {
                MediaSources = new[] { mediaSource },
                PlaySessionId = Guid.NewGuid().ToString("N")
            };

            context.Result = new ObjectResult(playbackInfoResponse);
        }
    }
}
