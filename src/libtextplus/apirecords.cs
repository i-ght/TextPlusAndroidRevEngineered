using LibCyStd;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace LibTextPlus
{
    // responses 

    public class TextPlusRegisterResponse
    {
        public string Username { get; }

        public TextPlusRegisterResponse(
            in string username)
        {
            Username = username;
        }
    }

    public class TextPlusGrantTicketResponse
    {
        [JsonRequired]
        [JsonProperty("ticket")]
        public string Ticket { get; }

        [JsonRequired]
        [JsonProperty("type")]
        public string Type { get; }

        public TextPlusGrantTicketResponse(
            string ticket,
            string type)
        {
            Ticket = ticket;
            Type = type;
        }
    }

    public class TptnLocale
    {
        [JsonRequired]

        [JsonProperty("localeId")]
        public string LocaleId { get; }

        [JsonProperty("country")]
        public string Country { get; }

        [JsonProperty("division")]
        public string Division { get; }

        [JsonProperty("subdivision")]
        public string Subdivision { get; }

        [JsonProperty("countryLabel")]
        public string CountryLabel { get; }

        [JsonProperty("divisionLabel")]
        public string DivisionLabel { get; }

        [JsonProperty("subdivisionLabel")]
        public string SubdivisionLabel { get; }

        [JsonProperty("available")]
        public bool Available { get; }

        public TptnLocale(
            string localeId,
            string country,
            string division,
            string subdivision,
            string countryLabel,
            string divisionLabel,
            string subdivisionLabel,
            bool available)
        {
            LocaleId = localeId;
            Country = country;
            Division = division;
            Subdivision = subdivision;
            CountryLabel = countryLabel;
            DivisionLabel = divisionLabel;
            SubdivisionLabel = subdivisionLabel;
            Available = available;
        }

        public override string ToString()
        {
            return $"{Country}~{Division}~{SubdivisionLabel}";
        }
    }

    public class TextPlusMatchablePersona
    {
        [JsonRequired]
        [JsonProperty("id")]
        public string Id { get; }
        [JsonRequired]
        [JsonProperty("jid")]
        public string Jid { get; }
        [JsonProperty("firstName")]
        public string FirstName { get; }
        [JsonProperty("LastName")]
        public string LastName { get; }
        [JsonRequired]
        [JsonProperty("handle")]
        public string Handle { get; }
        [JsonProperty("sex")]
        public string Sex { get; }
        [JsonProperty("lastSeen")]
        public long LastSeen { get; }
        [JsonIgnore]
        public DateTimeOffset LastSeenDateTime { get; }

        public TextPlusMatchablePersona(string id, string jid, string firstName, string lastName, string handle, string sex, long lastSeen)
        {
            Id = id;
            Jid = jid;
            FirstName = firstName;
            LastName = lastName;
            Handle = handle;
            Sex = sex;
            LastSeen = lastSeen;
            LastSeenDateTime = DateTimeOffset.FromUnixTimeMilliseconds(lastSeen);
        }
    }

    public class TextPlusMatchableSearchResponse : IEquatable<TextPlusMatchableSearchResponse>
    {
        public TextPlusMatchableSearchResponse(string id, string value, string type, TextPlusMatchablePersona persona, bool matchable, string status)
        {
            Id = id;
            Value = value;
            Type = type;
            Persona = persona;
            Matchable = matchable;
            Status = status;
        }

        [JsonRequired]
        [JsonProperty("id")]
        public string Id { get; }
        [JsonProperty("value")]
        public string Value { get; }
        [JsonProperty("type")]
        public string Type { get; }
        [JsonRequired]
        [JsonProperty("persona")]
        public TextPlusMatchablePersona Persona { get; }
        [JsonProperty("matchable")]
        public bool Matchable { get; }
        [JsonProperty("status")]
        public string Status { get; }

        public bool Equals(TextPlusMatchableSearchResponse other)
        {
            return other.Persona.Jid == Persona.Jid;
        }

        public override int GetHashCode()
        {
            var hashCode = 1340703851;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Persona.Jid);
            return hashCode;
        }
    }

    public class TextPlusRetrieveLocalesResponse
    {
        [JsonRequired]
        [JsonProperty("tptnLocales")]
        public ReadOnlyCollection<TptnLocale> TptnLocales { get; }

        [JsonProperty("requestCountry")]
        public string RequestCountry { get; }

        [JsonProperty("responseCountry")]
        public string ResponseCountry { get; }

        public TextPlusRetrieveLocalesResponse(
            ReadOnlyCollection<TptnLocale> tptnLocales,
            string requestCountry,
            string responseCountry)
        {
            TptnLocales = tptnLocales;
            RequestCountry = requestCountry;
            ResponseCountry = responseCountry;
        }
    }

    public class TextPlusDevice
    {
        [JsonProperty("id")]
        public string Id { get; }

        [JsonProperty("pushToken")]
        public string PushToken { get; }

        [JsonProperty("pushTokenType")]
        public string PushTokenType { get; }

        [JsonProperty("pushType")]
        public int PushType { get; }

        [JsonProperty("pushEnabled")]
        public bool PushEnabled { get; }

        [JsonProperty("model")]
        public string Model { get; }

        [JsonProperty("deviceUDID")]
        public string DeviceUDID { get; }

        [JsonProperty("appName")]
        public string AppName { get; }

        [JsonProperty("platform")]
        public string Platform { get; }

        [JsonProperty("platformOSVersion")]
        public string PlatformOSVersion { get; }

        [JsonProperty("appVersion")]
        public string AppVersion { get; }

        public TextPlusDevice(string id, string pushToken, string pushTokenType, int pushType, bool pushEnabled, string model, string deviceUDID, string appName, string platform, string platformOSVersion, string appVersion)
        {
            Id = id;
            PushToken = pushToken;
            PushTokenType = pushTokenType;
            PushType = pushType;
            PushEnabled = pushEnabled;
            Model = model;
            DeviceUDID = deviceUDID;
            AppName = appName;
            Platform = platform;
            PlatformOSVersion = platformOSVersion;
            AppVersion = appVersion;
        }
    }

    public class TextPlusEmbeddedDevices
    {
        [JsonRequired]

        [JsonProperty("devices")]
        public ReadOnlyCollection<TextPlusDevice> Devices { get; }

        public TextPlusEmbeddedDevices(ReadOnlyCollection<TextPlusDevice> devices)
        {
            Devices = devices;
        }
    }

    public class TextPlusRetrieveUserDeviceResponse
    {
        [JsonRequired]
        [JsonProperty("_embedded")]
        public TextPlusEmbeddedDevices Embedded { get; }

        public TextPlusRetrieveUserDeviceResponse(TextPlusEmbeddedDevices embedded)
        {
            Embedded = embedded;
        }
    }

    public class TextPlusUser
    {
        [JsonProperty("id")]
        public string Id { get; }

        public TextPlusUser(string id)
        {
            Id = id;
        }
    }

    public class TextPlusPrimaryPersona
    {
        [JsonRequired]
        [JsonProperty("user")]
        public TextPlusUser User { get; }

        [JsonRequired]
        [JsonProperty("jid")]
        public string Jid { get; }

        [JsonRequired]
        [JsonProperty("id")]
        public string Id { get; }

        public TextPlusPrimaryPersona(TextPlusUser user, string jid, string id)
        {
            User = user;
            Jid = jid;
            Id = id;
        }
    }

    public class TextPlusPersona
    {
        [JsonRequired]
        [JsonProperty("id")]
        public string Id { get; }

        [JsonRequired]
        [JsonProperty("jid")]
        public string Jid { get; }

        public TextPlusPersona(string id, string jid)
        {
            Id = id;
            Jid = jid;
        }
    }

    public class TextPlusCredential
    {
        [JsonProperty("username")]
        public string Username { get; }

        public TextPlusCredential(string username)
        {
            Username = username;
        }
    }


    public class TextPlusLocalUserInfoResponse
    {
        [JsonRequired]
        [JsonProperty("primaryPersona")]
        public TextPlusPrimaryPersona PrimaryPersona { get; }

        [JsonProperty("network")]
        public string Network { get; }

        [JsonRequired]
        [JsonProperty("personas")]
        public ReadOnlyCollection<TextPlusPersona> Personas { get; }

        [JsonRequired]
        [JsonProperty("devices")]
        public ReadOnlyCollection<TextPlusDevice> Devices { get; }

        [JsonProperty("status")]
        public string Status { get; }

        [JsonRequired]
        [JsonProperty("id")]
        public string Id { get; }

        [JsonProperty("country")]
        public string Country { get; }

        public TextPlusLocalUserInfoResponse(TextPlusPrimaryPersona primaryPersona, string network, ReadOnlyCollection<TextPlusPersona> personas, ReadOnlyCollection<TextPlusDevice> devices, string status, string id, string country)
        {
            PrimaryPersona = primaryPersona;
            Network = network;
            Personas = personas;
            Devices = devices;
            Status = status;
            Id = id;
            Country = country;
        }
    }

    public class TextPlusReqVerificationCodeResponse
    {
        [JsonProperty("createdDate")]
        public long CreatedDate { get; }

        [JsonProperty("lastModifiedDate")]
        public long LastModifiedDate { get; }

        [JsonProperty("value")]
        public string Value { get; }

        [JsonProperty("verificationType")]
        public string VerificationType { get; }

        [JsonProperty("verified")]
        public bool Verified { get; }

        [JsonProperty("expirationDate")]
        public long ExpirationDate { get; }

        public TextPlusReqVerificationCodeResponse(long createdDate, long lastModifiedDate, string value, string verificationType, bool verified, long expirationDate)
        {
            CreatedDate = createdDate;
            LastModifiedDate = lastModifiedDate;
            Value = value;
            VerificationType = verificationType;
            Verified = verified;
            ExpirationDate = expirationDate;
        }
    }

    public class TextPlusAllocPersonaResponse
    {
        [JsonProperty("id")]
        public string Id { get; }
        [JsonProperty("phoneNumber")]
        public string PhoneNumber { get; }
        [JsonProperty("country")]
        public string Country { get; }
        [JsonProperty("carrier")]
        public string Carrier { get; }
        [JsonProperty("status")]
        public string Status { get; }

        public TextPlusAllocPersonaResponse(string id, string phoneNumber, string country, string carrier, string status)
        {
            Id = id;
            PhoneNumber = phoneNumber;
            Country = country;
            Carrier = carrier;
            Status = status;
        }
    }

    // requests
    public class TextPlusRegisterDevInfo
    {
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
#pragma warning disable IDE1006 // Naming Styles
        public string platformOSVersion { get; set; }
        public string appName { get; set; }
        public string appVersion { get; set; }
        public string deviceUDID { get; set; }
        public string model { get; set; }
        public string platform { get; set; }
        public bool pushEnabled { get; set; }
        public string pushTokenType { get; set; }
        public int pushType { get; set; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
#pragma warning restore IDE1006 // Naming Styles

    }

    public class TextPlusRegisterReqContent
    {
        [JsonProperty("avatarUrl")]
        public string AvatarUrl { get; }

        [JsonProperty("country")]
        public string Country { get; }
        [JsonProperty("device")]
        public TextPlusRegisterDevInfo Device { get; }

        [JsonProperty("locale")]
        public string Locale { get; }

        [JsonProperty("network")]
        public string Network { get; }

        [JsonProperty("optin")]
        public int OptIn { get; }

        [JsonProperty("password")]
        public string Password { get; }

        [JsonProperty("tos")]
        public int Tos { get; }

        [JsonProperty("username")]
        public string Username { get; }

        public TextPlusRegisterReqContent(
            in string avatarUrl,
            in string country,
            TextPlusRegisterDevInfo dev,
            in string locale,
            in string network,
            in int optIn,
            in string password,
            in int tos,
            in string username)
        {
            AvatarUrl = avatarUrl;
            Country = country;
            Device = dev;
            Locale = locale;
            Network = network;
            OptIn = optIn;
            Password = password;
            Tos = tos;
            Username = username;
        }
    }

    public class TextPlusReqTicketGranterTicketReqContent
    {
        [JsonProperty("username")]
        public string Username { get; }

        [JsonProperty("password")]
        public string Password { get; }

        public TextPlusReqTicketGranterTicketReqContent(
            in string username,
            in string password)
        {
            Username = username;
            Password = password;
        }
    }

    public class TextPlusRequestServiceTicketReqContent
    {
        [JsonProperty("service")]
        public string Service { get; }

        [JsonProperty("ticketGrantingTicket")]
        public string TicketGrantingTicket { get; }

        public TextPlusRequestServiceTicketReqContent(
            string service,
            string ticketGrantingTicket)
        {
            Service = service;
            TicketGrantingTicket = ticketGrantingTicket;
        }
    }

    public class TextPlusSendDeviceReqContent
    {
        [JsonProperty("platformOSVersion")]
        public string PlatformOSVersion { get; }

        [JsonProperty("appName")]
        public string AppName { get; }

        [JsonProperty("appVersion")]
        public string AppVersion { get; }

        [JsonProperty("deviceUDID")]
        public string DeviceUDID { get; }

        [JsonProperty("model")]
        public string Model { get; }

        [JsonProperty("platform")]
        public string Platform { get; }

        [JsonProperty("pushEnabled")]
        public bool PushEnabled { get; }

        [JsonProperty("pushToken")]
        public string PushToken { get; }

        [JsonProperty("pushTokenType")]
        public string PushTokenType { get; }

        [JsonProperty("pushType")]
        public int PushType { get; }

        [JsonProperty("user")]
        public string User { get; }

        public TextPlusSendDeviceReqContent(
            string platformOSVersion,
            string appName,
            string appVersion,
            string deviceUDID,
            string model,
            string platform,
            bool pushEnabled,
            string pushToken,
            string pushTokenType,
            int pushType,
            string user)
        {
            PlatformOSVersion = platformOSVersion;
            AppName = appName;
            AppVersion = appVersion;
            DeviceUDID = deviceUDID;
            Model = model;
            Platform = platform;
            PushEnabled = pushEnabled;
            PushToken = pushToken;
            PushTokenType = pushTokenType;
            PushType = pushType;
            User = user;
        }
    }

    public enum VerificationType
    {
        EMAIL,
        PHONE
    }

    public class TextPlusSendVerificationInfoReqContent
    {
        [JsonProperty("user")]
        public string User { get; }
        [JsonProperty("value")]
        public string Value { get; }
        [JsonProperty("verificationType")]
        [JsonConverter(typeof(StringEnumConverter))]
        public VerificationType VerificationType { get; }

        public TextPlusSendVerificationInfoReqContent(
            string user,
            string value,
            VerificationType verificationType)
        {
            User = user;
            Value = value;
            VerificationType = verificationType;
        }
    }

    public class TextPlusAllocPersonaReqContent
    {
        [JsonProperty("deviceUdid")]
        public string DeviceUdid { get; }

        [JsonProperty("localeId")]
        public string LocaleId { get; }

        [JsonProperty("platform")]
        public string Platform { get; }

        [JsonProperty("pushToken")]
        public string PushToken { get; }

       // [JsonProperty("recaptchaCode")]
        //public string RecaptchaCode { get; }

        public TextPlusAllocPersonaReqContent(string deviceUdid, string localeId, string platform, string pushToken/*, string recaptchaCode*/)
        {
            DeviceUdid = deviceUdid;
            LocaleId = localeId;
            Platform = platform;
            PushToken = pushToken;
            //RecaptchaCode = recaptchaCode;
        }
    }

    public class TextPlusFindPersonaByJidUser
    {
        public TextPlusFindPersonaByJidUser(string id)
        {
            Id = id;
        }

        [JsonRequired]
        [JsonProperty("id")]
        public string Id { get; set; }
    }

    public class TextPlusFindPersonaByJidTptn
    {
        public TextPlusFindPersonaByJidTptn(string id, long createdDate, long lastModifiedDate, string phoneNumber, string country, string carrier, object lastUse, string status)
        {
            Id = id;
            CreatedDate = createdDate;
            LastModifiedDate = lastModifiedDate;
            PhoneNumber = phoneNumber;
            Country = country;
            Carrier = carrier;
            LastUse = lastUse;
            Status = status;
        }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("createdDate")]
        public long CreatedDate { get; set; }

        [JsonProperty("lastModifiedDate")]
        public long LastModifiedDate { get; set; }

        [JsonProperty("phoneNumber")]
        public string PhoneNumber { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("carrier")]
        public string Carrier { get; set; }

        [JsonProperty("lastUse")]
        public object LastUse { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }

    public class TextPlusFindPersonaByJidPersona
    {
        public TextPlusFindPersonaByJidPersona(TextPlusFindPersonaByJidUser user, string jid, string firstName, string lastName, long lastSeen, string sex, ReadOnlyCollection<TextPlusFindPersonaByJidTptn> tptns, string id, string handle)
        {
            User = user;
            Jid = jid;
            FirstName = firstName;
            LastName = lastName;
            LastSeen = lastSeen;
            Sex = sex;
            Tptns = tptns;
            Id = id;
            Handle = handle;
        }

        [JsonRequired]
        [JsonProperty("user")]
        public TextPlusFindPersonaByJidUser User { get; set; }

        [JsonRequired]
        [JsonProperty("jid")]
        public string Jid { get; set; }

        [JsonProperty("firstName")]
        public string FirstName { get; set; }

        [JsonProperty("lastName")]
        public string LastName { get; set; }

        [JsonProperty("lastSeen")]
        public long LastSeen { get; set; }

        [JsonProperty("sex")]
        public string Sex { get; set; }

        [JsonProperty("tptns")]
        public ReadOnlyCollection<TextPlusFindPersonaByJidTptn> Tptns { get; set; }

        [JsonRequired]
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonRequired]
        [JsonProperty("handle")]
        public string Handle { get; set; }
    }

    public class TextPlusFindPersonaByJidEmbedded
    {
        public TextPlusFindPersonaByJidEmbedded(ReadOnlyCollection<TextPlusFindPersonaByJidPersona> personae)
        {
            Personae = personae;
        }

        [JsonRequired]
        [JsonProperty("personae")]
        public ReadOnlyCollection<TextPlusFindPersonaByJidPersona> Personae { get; }
    }

    public class TextPlusFindPersonaByJidResponse
    {
        [JsonRequired]
        [JsonProperty("_embedded")]
        public TextPlusFindPersonaByJidEmbedded Embedded { get; }
        public TextPlusFindPersonaByJidResponse(TextPlusFindPersonaByJidEmbedded embedded)
        {
            Embedded = embedded;
        }
    }


    public class TextPlusMessageParty
    {
        public TextPlusMessageParty(long created, long updated, string conversationId, bool isDeleted, bool isDelivered, bool isSender, string jid, bool isRead, string id)
        {
            Created = created;
            Updated = updated;
            ConversationId = conversationId;
            IsDeleted = isDeleted;
            IsDelivered = isDelivered;
            IsSender = isSender;
            Jid = jid;
            IsRead = isRead;
            Id = id;
        }

        [JsonProperty("created")]
        public long Created { get; }

        [JsonProperty("updated")]
        public long Updated { get; }

        [JsonProperty("conversationId")]
        public string ConversationId { get; }

        [JsonProperty("isDeleted")]
        public bool IsDeleted { get; }

        [JsonProperty("isDelivered")]
        public bool IsDelivered { get; }

        [JsonProperty("isSender")]
        public bool IsSender { get; }

        [JsonProperty("jid")]
        public string Jid { get; }

        [JsonProperty("isRead")]
        public bool IsRead { get; }

        [JsonProperty("id")]
        public string Id { get; }
    }

    public class TextPlusMessage
    {
        public TextPlusMessage(long created, string conversationId, string body, string msgId, string messageType, ReadOnlyCollection<TextPlusMessageParty> messageParties, string fromJid, string mediaAssetType, string id)
        {
            Created = created;
            ConversationId = conversationId;
            Body = body;
            MsgId = msgId;
            MessageType = messageType;
            MessageParties = messageParties;
            FromJid = fromJid;
            MediaAssetType = mediaAssetType;
            Id = id;
        }

        [JsonProperty("created")]
        public long Created { get; }

        [JsonProperty("conversationId")]
        public string ConversationId { get; }

        [JsonProperty("body")]
        public string Body { get; }

        [JsonProperty("msgId")]
        public string MsgId { get; }

        [JsonProperty("messageType")]
        public string MessageType { get; }

        [JsonProperty("messageParties")]
        public ReadOnlyCollection<TextPlusMessageParty> MessageParties { get; }

        [JsonProperty("fromJid")]
        public string FromJid { get; }

        [JsonProperty("mediaAssetType")]
        public string MediaAssetType { get; }

        [JsonProperty("id")]
        public string Id { get; }
    }

    public class TextPlusRetrEmbedded
    {
        public TextPlusRetrEmbedded(ReadOnlyCollection<TextPlusMessage> messages)
        {
            Messages = messages;
        }

        [JsonProperty("messages")]
        public ReadOnlyCollection<TextPlusMessage> Messages { get; }
    }

    public class TextPlusRetrMsgsResponse
    {
        public TextPlusRetrMsgsResponse(TextPlusRetrEmbedded embedded)
        {
            Embedded = embedded;
        }

        [JsonProperty("_embedded")]
        public TextPlusRetrEmbedded Embedded { get; }
    }
}
