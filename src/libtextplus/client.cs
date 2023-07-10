using LibCyStd;
using LibCyStd.Net;
using LibCyStd.Seq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Threading.Tasks;

namespace LibTextPlus
{
    using StrKvp = ValueTuple<string, string>;

    public class TextPlusApiHttpCtx : IDisposable
    {
        public TextPlusApiReq ApiReq { get; }
        public HttpReq HttpReq { get; }
        public HttpResp HttpResp { get; }

        public TextPlusApiHttpCtx(TextPlusApiReq apiReq, HttpReq httpReq, HttpResp httpResp)
        {
            ApiReq = apiReq;
            HttpReq = httpReq;
            HttpResp = httpResp;
        }

        public void Dispose()
        {
            HttpReq.ContentBody?.Dispose();
            HttpResp.Dispose();
        }
    }

    public class TextPlusApiResult<TValue> : IDisposable
    {
        public TextPlusApiHttpCtx HttpCtx { get; }
        public TValue Value { get; }

        public TextPlusApiResult(TValue value, TextPlusApiHttpCtx httpCtx)
        {
            Value = value;
            HttpCtx = httpCtx;
        }

        public void Dispose()
        {
            HttpCtx.Dispose();
        }
    }

    public class TextPlusAndroidDevice
    {
        public string Manufacturer { get; }
        public string Model { get; }
        public string Device { get; }
        public string OsVersion { get; }

        public TextPlusAndroidDevice(
            in string manufacturer,
            in string model,
            in string device,
            in string osVersion)
        {
            Manufacturer = manufacturer;
            Model = model;
            Device = device;
            OsVersion = osVersion;
        }

        public static Option<TextPlusAndroidDevice> TryParse(string input)
        {
            var sp = input.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            return sp.Length switch
            {
                4 => Option.Some(new TextPlusAndroidDevice(sp[0], sp[1], sp[2], sp[3])),
                _ => Option.None
            };
        }
    }

    public abstract class TextPlusSession
    {
        public TextPlusAndroidDevice Device { get; }

        [JsonIgnore]
        public Proxy Proxy { get; set; }
        public string AndroidId { get; }

        protected TextPlusSession(
            in TextPlusAndroidDevice device,
            in Proxy proxy,
            in string androidId)
        {
            Device = device;
            Proxy = proxy;
            AndroidId = androidId;
        }
    }

    public class AnonTextPlusSession : TextPlusSession
    {
        public AnonTextPlusSession(
            in TextPlusAndroidDevice device,
            in Proxy proxy,
            in string androidId) : base(device, proxy, androidId)
        {
        }
    }

    public class AuthTextPlusSession : TextPlusSession
    {
        public string Username { get; }
        public string Password { get; }
        public string AuthToken { get; set; }
        public string PushToken { get; set; }
        public string UserId { get; }
        public string PrimaryPersonaId { get; }
        public string PhoneNumber { get; set; }
        public string Jid { get; }

        public AuthTextPlusSession(
            in TextPlusAndroidDevice device,
            in Proxy proxy,
            in string androidId,
            in string username,
            in string password,
            in string authToken,
            in string pushToken,
            in string userId,
            in string primaryPersonaId,
            in string phoneNumber,
            in string jid) : base(device, proxy, androidId)
        {
            Username = username;
            Password = password;
            AuthToken = authToken;
            PushToken = pushToken ?? "";
            UserId = userId;
            PrimaryPersonaId = primaryPersonaId;
            PhoneNumber = phoneNumber ?? "";
            Jid = jid;
        }

        public static Option<AuthTextPlusSession> TryParseFromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<AuthTextPlusSession>(json);
            }
            catch (JsonException)
            {
                return Option.None;
            }
        }
    }

    public sealed class TextPlusApiReq
    {
        public string Method { get; }
        public string Uri { get; }
        public Version HttpVersion { get; }

        private TextPlusApiReq(
            in string method,
            in string uri,
            in Version httpVersion)
        {
            Method = method;
            Uri = uri;
            HttpVersion = httpVersion;
        }

        public static TextPlusApiReq CreateHttp11Req(in string method, in string uri) => new TextPlusApiReq(method, uri, LibCyStd.HttpVersion.Http11);
        public static TextPlusApiReq CreateHttp2Req(in string method, in string uri) => new TextPlusApiReq(method, uri, LibCyStd.HttpVersion.Http2);

        public override string ToString()
        {
            return $"{Method} {Uri} {HttpVersion}";
        }
    }

    public static class TextPlusApiClient
    {
        private static string SerializeJson(object o) => JsonConvert.SerializeObject(o);

        private static TJsonObject DeserializeJson<TJsonObject>(string json)
        {
            try
            {
                var result = JsonConvert.DeserializeObject<TJsonObject>(json);
                if (result == null)
                    ExnModule.InvalidOp($"json deserialization returned null for json string: {json}");
                return result;
            }
            catch (JsonException e)
            {
                var txt = json.Length > 1024 ? json.Substring(0, 1024) : json;
                throw new InvalidOperationException($"json exception occured trying to deserialize string: {Environment.NewLine} {txt}. {e.GetType().Name} ~ {e.Message}", e);
            }
        }

        private static ReadOnlyCollection<StrKvp> ApiReqHeaders(
            TextPlusSession session)
        {
            var headers = ListModule.OfSeq(new[]
            {
                ("platform", "android"),
                ("market", "GooglePlay"),
                ("appversion", Constants.Version),
                ("udid", session.AndroidId),
                ("device", session.Device.Device),
                ("sku", "com.gogii.textplus"),
                ("network", "nextplus"),
                ("carrier", "none"),
                ("Accept-Encoding", "gzip"),
                ("User-Agent", Constants.UserAgent),
                ("Content-Type",  "application/json; charset=UTF-8"),
                ("Connection", "Keep-Alive")
            });

            if (session is AuthTextPlusSession a)
            {
                headers.Add(("Authorization", $"CASST {a.AuthToken}"));
            }

            return ReadOnlyCollectionModule.OfSeq(headers);
        }

        private static async Task<TextPlusApiHttpCtx> RetrApiResp<TReqContent>(
            TextPlusApiReq apiReq,
            TextPlusSession session,
            Option<TReqContent> content)
        {
            if (session.Proxy == null)
                ExnModule.InvalidOp("Forgot to set proxy.");

            var req = new HttpReq(apiReq.Method, apiReq.Uri)
            {
                Headers = ApiReqHeaders(session),
                Proxy = session.Proxy!,
                ContentBody = content switch
                {
                    (true, var value) => new StringHttpContent(SerializeJson(value)),
                    _ => null
                },
                ProtocolVersion = apiReq.HttpVersion
            };
            try
            {
                var resp = await HttpModule.RetrRespAsync(req).ConfigureAwait(false);
                return new TextPlusApiHttpCtx(apiReq, req, resp);
            }
            finally
            {
                req.ContentBody?.Dispose();
            }
        }

        private static Option<string> TryGetHeaderValue(in HttpResp resp, in string headerKey)
        {
            if (!resp.Headers.ContainsKey(headerKey)
                || resp.Headers[headerKey].Count != 1
                || string.IsNullOrWhiteSpace(resp.Headers[headerKey][0]))
            {
                return Option.None;
            }
            return resp.Headers[headerKey][0];
        }

        private static void CheckExpect(HttpResp resp, Func<HttpResp, bool> predicate, Action onUnexpected)
        {
            if (!predicate(resp)) onUnexpected();
        }

        private static void ValidateResponse(
            TextPlusApiHttpCtx reqCtx,
            HttpStatusCode expectedStatusCode)
        {
            CheckExpect(
                reqCtx.HttpResp,
                resp => resp.StatusCode == expectedStatusCode,
                () => TextPlusModule.TextPlusApiEx(
                    $"error occured trying to retrieve response for request {reqCtx.ApiReq}. textplus api server returned unexpected status code {reqCtx.HttpResp.StatusCode}.",
                    reqCtx
                )
            );
        }

        public static async Task<TextPlusApiResult<TextPlusRegisterResponse>> Register(
            AnonTextPlusSession session,
            TextPlusRegisterReqContent registerInfo)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("POST", "https://ums.prd.gii.me/registration/mobile");
            var result = await RetrApiResp(apiReq, session, Option.Some(registerInfo)).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.Created);
            static string ParseRegisterResp(TextPlusApiHttpCtx result)
            {
                var usernameResult = TryGetHeaderValue(result.HttpResp, "username");
                if (usernameResult.IsNone) {
                    TextPlusModule.TextPlusApiEx(
                        $"error occured trying to retrieve response for request {result.ApiReq}. response did not contain expected header with key 'username'.", result
                    );
                }

                return result.HttpResp.Headers["username"][0];
            }
            var issuedUsername = ParseRegisterResp(result);
            return new TextPlusApiResult<TextPlusRegisterResponse>(
                new TextPlusRegisterResponse(issuedUsername),
                result
            );
        }

        public static async Task<TextPlusApiResult<TextPlusGrantTicketResponse>> RequestTicketGranterTicket(
            TextPlusSession session,
            TextPlusReqTicketGranterTicketReqContent loginInfo)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("POST", "https://cas.prd.gii.me/v2/ticket/ticketgranting/user");
            var result = await RetrApiResp(apiReq, session, Option.Some(loginInfo)).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.Created);
            var value = DeserializeJson<TextPlusGrantTicketResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusGrantTicketResponse>(value, result);
        }

        public static async Task<TextPlusApiResult<TextPlusGrantTicketResponse>> RequestServiceTicket(
            TextPlusSession session,
            TextPlusRequestServiceTicketReqContent ticketGrantingInfo)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("POST", "https://cas.prd.gii.me/v2/ticket/service");
            var result = await RetrApiResp(apiReq, session, Option.Some(ticketGrantingInfo)).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.Created);
            var value = DeserializeJson<TextPlusGrantTicketResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusGrantTicketResponse>(value, result);
        }

        public static async Task<TextPlusApiHttpCtx> SendDeviceInfo(
            AuthTextPlusSession session,
            TextPlusSendDeviceReqContent deviceInfo)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("POST", "https://ums.prd.gii.me/v2/devices");
            var result = await RetrApiResp(apiReq, session, Option.Some(deviceInfo)).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.NoContent);
            return result;
        }

        public static async Task<TextPlusApiResult<TextPlusRetrieveLocalesResponse>> RetrieveLocales(
            AuthTextPlusSession session)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("GET", "https://ums.prd.gii.me/tptnLocales?country=US");
            var result = await RetrApiResp<bool>(apiReq, session, Option.None).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<TextPlusRetrieveLocalesResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusRetrieveLocalesResponse>(value, result);
        }

        public static async Task<TextPlusApiResult<ReadOnlyCollection<TextPlusMatchableSearchResponse>>> SearchMatchable(
            AuthTextPlusSession session,
            string username)
        {
            var query = ReadOnlyCollectionModule.OfSeq(new[] {
                ("value", username),
                ("projection", "inline"),
                ("network", "nextplus")
            });
            var apiReq = TextPlusApiReq.CreateHttp2Req("GET", $"https://ums.prd.gii.me/matchables/search/findByValueIn?{HttpUtils.UrlEncodeSeq(query)}");
            var result = await RetrApiResp<bool>(apiReq, session, Option.None).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<ReadOnlyCollection<TextPlusMatchableSearchResponse>>(result.HttpResp.Content);
            return new TextPlusApiResult<ReadOnlyCollection<TextPlusMatchableSearchResponse>>(value, result);
        }

        public static async Task<TextPlusApiResult<TextPlusReqVerificationCodeResponse>> RequestVerificationCode(
            AuthTextPlusSession session,
            TextPlusSendVerificationInfoReqContent info)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("POST", "https://ums.prd.gii.me/verifications");
            var result = await RetrApiResp(apiReq, session, Option.Some(info)).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<TextPlusReqVerificationCodeResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusReqVerificationCodeResponse>(value, result);
        }

        public static async Task<TextPlusApiHttpCtx> VerifyPhoneCode(
            AuthTextPlusSession session,
            string phoneNumberWithCountryCode,
            string code)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req(
                "POST",
                $"https://ums.prd.gii.me/verifiedInfo/confirmPhone/{code}/{HttpUtils.UrlEncode(phoneNumberWithCountryCode)}"
            );
            var result = await RetrApiResp(apiReq, session, Option.Some<object>(null!)).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.NoContent);
            return result;
        }

        public static async Task<TextPlusApiResult<TextPlusRetrieveUserDeviceResponse>> RetrieveDevices(
            AuthTextPlusSession session)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("GET", $"https://ums.prd.gii.me/users/{session.UserId}/devices");
            var result = await RetrApiResp<bool>(apiReq, session, Option.None).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<TextPlusRetrieveUserDeviceResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusRetrieveUserDeviceResponse>(value, result);
        }

        public static async Task<TextPlusApiResult<TextPlusAllocPersonaResponse>> AllocPersona(
            AuthTextPlusSession session,
            TextPlusAllocPersonaReqContent content)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("POST", $"https://ums.prd.gii.me/personas/{session.PrimaryPersonaId}/tptn/allocate");
            var result = await RetrApiResp(apiReq, session, Option.Some(content)).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.Created);
            var value = DeserializeJson<TextPlusAllocPersonaResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusAllocPersonaResponse>(value, result);
        }

        public static async Task<TextPlusApiResult<TextPlusFindPersonaByJidResponse>> FindPersonaByJid(
            AuthTextPlusSession session,
            string jid)
        {
            var query = ReadOnlyCollectionModule.OfSeq(new[] {
                ("jid", jid),
                ("network", "nextplus"),
                ("size", "1"),
                ("projection", "inline")
            });
            var apiReq = TextPlusApiReq.CreateHttp2Req("GET", $"https://ums.prd.gii.me/personas/search/findByJidIn?{HttpUtils.UrlEncodeSeq(query)}");
            var result = await RetrApiResp(apiReq, session, Option.NoneF<bool>()).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<TextPlusFindPersonaByJidResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusFindPersonaByJidResponse>(value, result);
        }

        public static async Task<TextPlusApiResult<TextPlusLocalUserInfoResponse>> RetrLocalInfo11(AuthTextPlusSession session, string userId)
        {
            var apiReq = TextPlusApiReq.CreateHttp11Req("GET", $"https://ums.app.nextplus.me/users/{userId}?projection=inline");
            var result = await RetrApiResp<bool>(apiReq, session, Option.None).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<TextPlusLocalUserInfoResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusLocalUserInfoResponse>(value, result);
        }

        public static async Task<TextPlusApiResult<TextPlusLocalUserInfoResponse>> RetrieveLocalInfo(AuthTextPlusSession session)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("GET", "https://ums.prd.gii.me/me?projection=inline");
            var result = await RetrApiResp<bool>(apiReq, session, Option.None).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<TextPlusLocalUserInfoResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusLocalUserInfoResponse>(value, result);
        }

        public static async Task<TextPlusApiResult<TextPlusLocalUserInfoResponse>> RetrieveUserInfo(AuthTextPlusSession session, string userId)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("GET", $"https://ums.prd.gii.me/users/{userId}?projection=inline");
            var result = await RetrApiResp<bool>(apiReq, session, Option.None).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<TextPlusLocalUserInfoResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusLocalUserInfoResponse>(value, result);
        }

        public static async Task<TextPlusApiResult<DeetUserInfoResp>> RetrieveUserInfoDeetz(AuthTextPlusSession session, string userId)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("GET", $"https://ums.prd.gii.me/users/{userId}?projection=inline");
            var result = await RetrApiResp<bool>(apiReq, session, Option.None).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<DeetUserInfoResp>(result.HttpResp.Content);
            return new TextPlusApiResult<DeetUserInfoResp>(value, result);
        }


        public static async Task<TextPlusApiResult<TextPlusRetrMsgsResponse>> RetrieveMessages(
            AuthTextPlusSession session,
            DateTimeOffset olderThenOrEq = default,
            int size = 40,
            int numPerConvo = 1)
        {
            var dt = $"{olderThenOrEq.ToString("u").Replace(' ', 'T').TrimEnd('Z')}.{RandomModule.Next(9)}Z";
            var apiReq = TextPlusApiReq.CreateHttp2Req("GET", $"https://mhs.prd.gii.me/messages/search/findMostRecentByJidAndCreatedLessThanOrEqual?jid={session.Jid}&olderThanOrEq={dt}&size={size}&numPerConvo={numPerConvo}&projection=inline");
            var result = await RetrApiResp<bool>(apiReq, session, Option.None).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.OK);
            var value = DeserializeJson<TextPlusRetrMsgsResponse>(result.HttpResp.Content);
            return new TextPlusApiResult<TextPlusRetrMsgsResponse>(value, result);
        }

        public static async Task MarkMsgRead(
            AuthTextPlusSession session,
            string jid,
            string convoId)
        {
            var apiReq = TextPlusApiReq.CreateHttp2Req("PUT", $"https://mhs.prd.gii.me/messages/read?jid={jid}&conversationId={convoId}&projection=inline&readStatus=true");
            using var result = await RetrApiResp(apiReq, session, Option.Some<object>(null!)).ConfigureAwait(false);
            ValidateResponse(result, HttpStatusCode.NoContent);
        }
    }
}
