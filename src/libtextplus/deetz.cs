using System;
using System.Collections.Generic;
using System.Text;

namespace LibTextPlus
{
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
    public class DeetUserInfoResp
    {
        public long? lastModifiedDate { get; set; }
        public string network { get; set; }
        public DeetPersona[] personas { get; set; }
        public long? lastSeen { get; set; }
        public long? createdDate { get; set; }
        public long? lastLogin { get; set; }
        public DeetPrimPersona primaryPersona { get; set; }
        public object dob { get; set; }
        public DeetDevice[] devices { get; set; }
        public Matchable[] matchables { get; set; }
        public Balance[] balances { get; set; }
        public object[] verifications { get; set; }
        public object blockedJids { get; set; }
        public string currency { get; set; }
        public string locale { get; set; }
        public string id { get; set; }
        public string country { get; set; }
        public string status { get; set; }
    }

    public class DeetPrimPersona
    {
        public User user { get; set; }
        public string jid { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public long lastSeen { get; set; }
        public string avatarUrl { get; set; }
        public string sex { get; set; }
        public Tptn[] tptns { get; set; }
        public string id { get; set; }
        public string handle { get; set; }
        public string displayName { get; set; }
    }

    public class User
    {
        public string id { get; set; }
    }

    public class Tptn
    {
        public string id { get; set; }
        public long createdDate { get; set; }
        public long lastModifiedDate { get; set; }
        public string phoneNumber { get; set; }
        public string country { get; set; }
        public string carrier { get; set; }
        public object lastUse { get; set; }
        public string status { get; set; }
    }

    public class DeetPersona
    {
        public string id { get; set; }
        public long createdDate { get; set; }
        public long lastModifiedDate { get; set; }
        public string jid { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string displayName { get; set; }
        public string handleBase { get; set; }
        public int handleIncrement { get; set; }
        public string handle { get; set; }
        public string avatarUrl { get; set; }
        public string sex { get; set; }
        public long lastSeen { get; set; }
    }

    public class DeetDevice
    {
        public string model { get; set; }
        public int pushType { get; set; }
        public string deviceUDID { get; set; }
        public string pushToken { get; set; }
        public string appName { get; set; }
        public string appVersion { get; set; }
        public string platformOSVersion { get; set; }
        public string platform { get; set; }
        public string ringTone { get; set; }
        public string textTone { get; set; }
        public bool pushEnabled { get; set; }
        public string pushTokenType { get; set; }
        public object voipPushToken { get; set; }
        public string id { get; set; }
        public object status { get; set; }
        public object lastAccess { get; set; }
    }

    public class Matchable
    {
        public object expirationDate { get; set; }
        public bool matchable { get; set; }
        public string value { get; set; }
        public string id { get; set; }
        public string type { get; set; }
        public string status { get; set; }
    }

    public class Balance
    {
        public string id { get; set; }
        public long createdDate { get; set; }
        public long lastModifiedDate { get; set; }
        public float value { get; set; }
        public string currencyType { get; set; }
        public string creditType { get; set; }
        public string balanceType { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
}
