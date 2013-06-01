using System;
using System.Linq;
using System.Text;
using System.Net;
using System.Web;
using System.Xml.XPath;
using System.Data;
using System.IO;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Intuit.QuickBase.Client;
using Intuit.QuickBase.Core;
using Newtonsoft.Json;

//
// qb2sprintly is not a ready-to-use script. Program.cs needs configuration and mappings below; for details see: 
// https://github.com/erickrivas/qb2sprintly
//
namespace qb2sprintly
{

class Program
{

 static void Main(string[] args)
{
    System.Console.WriteLine("");
    System.Console.WriteLine("qb2sprintly - C# script to sync from QuickBase Issues to Sprint.ly Items");
    System.Console.WriteLine("");  

    if (args.Length < 1 || string.IsNullOrEmpty(args[0]))
    {
        Console.WriteLine("Syntax: qb2sprintly [-Issues] [sprint]");
        return;
    }

    if (args[0].Equals("-Issues", StringComparison.OrdinalIgnoreCase))
    {
        string sprint = String.Empty;
        if (args.Length>1 && !String.IsNullOrEmpty(args[1])) sprint = args[1];
        ConvertIssuesFromQuickBaseToSprintly(sprint);
        return;
    }

}

/*
 * Some constants needed for the QuickBase C# SDK
 * 
*/
 public const string QBDBID = "acmeqbdbid";
 public const string QBTOKEN = "acmeqbtoken";
 public const string QBUSERNAME = "acmeqbusername";
 public const string QBPASSWORD = "acmeqbpassword";
 public const string QBISSUESID = "acmeqbissuesid";
 public const string QBDOMAIN = "acme.quickbase.com";


 // Some Sprintly constants needed for the REST API
 public const string SPRINTLYENDPOINT = "https://sprint.ly";
 public const string SPRINTLYUSERNAME = "someuser@acme.com";
 public const string SPRINTLYAPIKEY = "acmeproductsprintlyapikeygoeshere";

 static private bool ConvertIssuesFromQuickBaseToSprintly(string sprint)
 {
     var client = Intuit.QuickBase.Client.QuickBase.Login(QBUSERNAME, QBPASSWORD, QBDOMAIN);
     var application = client.Connect(QBDBID, QBTOKEN);
     var table = application.GetTable(QBISSUESID);
     table.Query();
     foreach (var record in table.Records)
     {
         // the Milestone field for a QuickBase Issue (could be any custom field) maps to the sprint tag (e.g. "Sprint 27")
         // use Record["Milestone"], record["Record ID#"], etc. here -- see quickbase_issue_name_value_collection.png
         if ((String.IsNullOrEmpty(sprint) || record["Milestone"].Equals(sprint, StringComparison.OrdinalIgnoreCase)) &&
             (record["Type"].Equals("product", StringComparison.OrdinalIgnoreCase))) { // pull-in only QB "Issues" with Issue.Type of "Product"
            string id = GetSprintlyItemByTags("QB#"+record["Record ID#"],record);
            CreateUpdateSprintlyItem(sprint,record,id);
         }
     }
     client.Logout();
     return true;
 }

/*
    * Sprintly POST API for creating or updating items:
    * 
    * POST
    Create a new item for the given product. This endpoint allows you to create new items within your products. It will return the newly created item on success.

    Arguments:
    number (string, optional) The Sprintly item number to update; if left blank or empty then it creates a new item
    type (string, required) What kind of item you'd like to create. Only story, task, defect, and test are valid values.
    title (string, required for task, defect, and test) The title of item.
    who (string, required for story) The who part of the story.
    what (string, required for story) The what part of the story.
    why (string, required for story) The why part of the story.
    description (string) A description detailing the item. Markdown is allowed.
    score (string) The initial score of the document. Only ~, S, M, L, and XL are valid values.
    status (string): Status of the new item. Default is backlog. Only backlog, in-progress, completed, and accepted are valid values.
    assigned_to (integer) The user's id which the item should assigned to.
    tags (string) A comma separated list of tags to assign to the item (e.g. foo,bar,some other tag,baz).
*/
 static public string CreateUpdateSprintlyItem(string sprint, IQRecord record, string id)
 { 
     Dictionary<string,string> item = new Dictionary<string,string>();
     if (!String.IsNullOrEmpty(id)) item["id"] = id;
     //item["qbid"] = record["Record ID#"];
     //item["qbtype"] = record["Type"];
     item["type"] = ConvertToSprintlyType(record["Type"], record["Change Type"]);
     item["title"] = record["Description"];
     //item["who"] = String.Empty;
     //item["what"] = String.Empty;
     //item["why"] = String.Empty;
     item["description"] = record["Details"];
     item["score"] = ConvertToSprintlyScore(record["Effort"]);
     item["status"] = ConvertToSprintlyStatus(record["Status"]);
     item["assigned_to"] = ConvertToSprintlyAssignedTo(record["Assigned"]);
     item["tags"] = ConvertToSprintlyTags(sprint,record);
     if (String.IsNullOrEmpty(item["tags"])) return string.Empty; // do not add an item with no tags
     foreach (var keyvalue in item) {
        Console.WriteLine("Item[{0}] = {1}", keyvalue.Key,keyvalue.Value);
     }
     Console.WriteLine(String.Empty);

     String createItemRequestString = String.Format(
          "/api/products/12011/items.json" +
          "?title={0}"
         /*+ "&who={2}"
         + "&what={3}"
         + "&why={4}"*/
         + "&description={1}"
         + "&score={2}"
         + "&status={3}"
         + "&assigned_to={4}"
         + "&tags={5}"
         , PercentEncodeRfc3986(item["title"]), /*item["who"], item["what"], item["why"],*/ PercentEncodeRfc3986(item["description"]), 
           item["score"], item["status"], PercentEncodeRfc3986(item["assigned_to"]), PercentEncodeRfc3986(item["tags"]));
     if (!String.IsNullOrEmpty(id))
     {
         createItemRequestString = createItemRequestString.Replace("items.json", "items/" + id + ".json");
     }
     else
     {
         createItemRequestString += "&type=" + item["type"];
     }
     return SprintlyAPIRequest("POST",createItemRequestString);
 }

 static public string GetSprintlyItemByTags(string tags, IQRecord record)
 {
     String getItemRequestString = String.Format("/api/products/12011/items.json?tags={0}&type={1}&status=backlog,in-progress,completed,accepted", 
            PercentEncodeRfc3986(tags),
            ConvertToSprintlyType(record["Type"], record["Change Type"]));
        
     string jsonResponse = SprintlyAPIRequest("GET",getItemRequestString);
     if (String.IsNullOrEmpty(jsonResponse)) return String.Empty;
     // TODO: sort of a hack to get the Sprintly Item number. Refactor to serialize JSON to C# Item and use JsonConvert.DeserializeObject<Item>(json);
     int ix = jsonResponse.IndexOf("\"number\":") + 9;
     if (ix == -1) return String.Empty;
     string id = jsonResponse.Substring(ix);
     string[] s = id.Split(',');
     id = s[0];
     //Console.WriteLine("******* Id: " + id);
     return id;
 }

private static string SprintlyAPIRequest(string method, string url)
{
     try
     {
         WebRequest req = WebRequest.Create(SPRINTLYENDPOINT+url);
         req.Method = method;
         req.Timeout = 10000;
         req.ContentType = "application/x-www-form-urlencoded";
         req.Headers.Add("X-Requested-With", "XMLHttpRequest");
         SetBasicAuthHeader(req, SPRINTLYUSERNAME, SPRINTLYAPIKEY);

         if (req.Method == "POST")
         {
             Int32 ix = url.IndexOf('?');
             ASCIIEncoding encoding = new ASCIIEncoding();
             byte[] bytes = encoding.GetBytes(url.Substring(ix + 1));
             req.ContentLength = bytes.Length;
             Stream newStream = req.GetRequestStream();
             newStream.Write(bytes, 0, bytes.Length);
         }

         WebResponse response = req.GetResponse();
         StreamReader reader = new StreamReader(response.GetResponseStream());
         string json = reader.ReadToEnd();
         if ((json==null) || (json=="[]")) return null;
         return json;
     }
     catch (Exception e)
     {
         System.Console.WriteLine("Caught Exception: " + e.Message);
         System.Console.WriteLine("Stack Trace: " + e.StackTrace);
     }
     return null;
 }

private static void SetBasicAuthHeader(WebRequest request, String userName, String userPassword)
{
    string authInfo = userName + ":" + userPassword;
    authInfo = Convert.ToBase64String(Encoding.Default.GetBytes(authInfo));
    request.Headers["Authorization"] = "Basic " + authInfo;
}

 static private string PercentEncodeRfc3986(string str)
 {
     str = HttpUtility.UrlEncode(str, System.Text.Encoding.UTF8);
     str = str.Replace("'", "%27").Replace("(", "%28").Replace(")", "%29").Replace("*", "%2A").Replace("!", "%21").Replace("%7e", "~").Replace("+", "%20");

     StringBuilder sbuilder = new StringBuilder(str);
     for (int i = 0; i < sbuilder.Length; i++)
     {
         if (sbuilder[i] == '%')
         {
             if (Char.IsLetter(sbuilder[i + 1]) || Char.IsLetter(sbuilder[i + 2]))
             {
                 sbuilder[i + 1] = Char.ToUpper(sbuilder[i + 1]);
                 sbuilder[i + 2] = Char.ToUpper(sbuilder[i + 2]);
             }
         }
     }
     return sbuilder.ToString();
 }

 // Sprintly tags (string) A comma separated list of tags to assign to the item (e.g. foo,bar,some other tag,baz).
 static private string ConvertToSprintlyTags(string sprint, IQRecord record)
 {
     string tags = String.Format("{0},QB#{1}", sprint, record["Record ID#"]);   // tag with the QB Issue # for auto-sync
     if (!String.IsNullOrEmpty(record["Product Area"])) tags += String.Format(",{0}", record["Product Area"]);
     if (!String.IsNullOrEmpty(record["Priority"])) tags += String.Format(",{0}", record["Priority"]);
     if (record["Change Type"] == "Bug") tags += String.Format(",S{0}", record["Severity"]);
     if (!String.IsNullOrEmpty(record["Resolution"])) tags += String.Format(",{0}", record["Resolution"]);
     return tags.ToLower();
 }

// Sprintly score (string) The  score (T-Shirt size estimate) of the item. Only ~, S, M, L, and XL are valid values.
static private string ConvertToSprintlyScore(string effort)
{
    string score = "~";
    switch (effort)
    {
        case "":
        case "XXS":
            score = "~";
            break;
        case "XS":
        case "S":
            score = "S";
            break;
        case "M":
        case "L":
            score = "M";
            break;
        case "XL":
            score = "L";
            break;
        case "XXL":
            score = "XL";
            break;
        default:
            score = "~";
            break;
    }
    return score;
}

// Sprintly valid status values: backlog, in-progress, completed, and accepted
static private string ConvertToSprintlyStatus(string status)
{
    switch (status.ToLower())
    {
        case "":
        case "assigned":
            status = "backlog";
            break;
        case "in progress":
            status = "in-progress";
            break;
        case "unit tested":
            status = "completed";
            break;
        case "verified":
            status = "accepted";
            break;
        case "closed":
            status = "accepted";
            break;
        default:
            status = "backlog";
            break;
    }
    return status;
}

// Sprintly item type, only one of: story, task, defect, and test are valid values.
static private string ConvertToSprintlyType(string issueType, string changeType)
{
    if (issueType.ToLower() != "product") return "task";
    switch (changeType.ToLower())
    {
        case "bug":
            issueType = "defect";
            break;
        case "enhancement":
            issueType = "task";
            break;
        default:
            issueType = "task";
            break;
    }
    return issueType;
}

// Sprintly mapping of QB users to Sprintly user id's. The syntax looks something like: { "56692589.test", "27000" },
static Dictionary<string,string> UserMappings = new Dictionary<string,string>() {
};

// Sprintly assigned_to (integer) The user's id which the item should be assigned to.
// https://sprint.ly/api/products/[productid]/people.json
static public string ConvertToSprintlyAssignedTo(string assigned)
{
    //return assigned;
    if (String.IsNullOrEmpty(assigned)) return String.Empty;
    if (UserMappings.ContainsKey(assigned)) return UserMappings[assigned];
    assigned = String.Empty; // uncomment this out to see the QB user id to add it to the above user mappings
    return assigned;
}

}
}
