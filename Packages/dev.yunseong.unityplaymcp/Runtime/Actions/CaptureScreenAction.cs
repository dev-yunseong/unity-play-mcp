using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Capture;
using UnityEngine;
using static UnityPlayMcp.ActionParameters;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp
{
    [UnityAction("capture_screen", "Capture the screen or the area of one object.", "[targetId?, options?]")]
    internal sealed class CaptureScreenAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (Context.Capturer == null)
            {
                completed(ActionResultDto.Failure(
                    actionId, "This build cannot capture the screen."));
                yield break;
            }

            if (!CaptureRequestReader.TryRead(parameters, out var request, out var paramsError))
            {
                completed(ActionResultDto.Failure(actionId, paramsError));
                yield break;
            }

            Rect? pixelRect = null;
            Rect? requestedRect = null;
            var clipped = false;
            if (!request.IsFullScreen)
            {
                var targetId = request.TargetId.Value;
                if (!Context.TargetLookup.TryGetTarget(targetId, out var target))
                {
                    completed(ActionResultDto.Failure(actionId, UnknownTarget(targetId)));
                    yield break;
                }

                var screen = new Rect(0f, 0f, Mathf.Max(2, Screen.width), Mathf.Max(2, Screen.height));
                if (!CaptureRect.TryResolve(target.RectTransform, request.Padding, screen, out var region))
                {
                    completed(ActionResultDto.Failure(
                        actionId, "Target is entirely off screen: " + targetId));
                    yield break;
                }

                pixelRect = region.PixelRect;
                requestedRect = region.Requested;
                clipped = region.Clipped;
            }

            var image = default(CapturedImage);
            yield return Context.Capturer.Capture(request, pixelRect, captured => image = captured);
            if (!image.IsSuccess)
            {
                completed(ActionResultDto.Failure(actionId, image.Error));
                yield break;
            }

            completed(ActionResultDto.Success(actionId, new CaptureResultDto
            {
                MimeType = request.ContentType,
                Width = image.Width,
                Height = image.Height,
                TargetId = request.TargetId,
                Clipped = clipped,
                Screen = new CaptureScreenSizeDto { Width = image.ScreenWidth, Height = image.ScreenHeight },
                Region = CaptureRect.TopLeft(image.Source, image.ScreenHeight),
                RequestedRegion = clipped && requestedRect.HasValue
                    ? CaptureRect.TopLeft(requestedRect.Value, image.ScreenHeight)
                    : null,
                Scale = CaptureRect.Scale(image.Source, image.Width, image.Height),
                Frame = image.Frame,
                Scene = image.Scene,
                Data = Convert.ToBase64String(image.Bytes)
            }));
        }
    }
}
