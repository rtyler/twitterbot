using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using System.Xml;
using Rss;

public class Twitterbot
{
	public const string TwitterUrl = "http://twitter.com/statuses/update.xml";
	public const string TinyUrl = "http://urlenco.de/PostJSON.aspx?encode=";
	public const int TwitterMax = 3; // maximum number of feed updates to pull
	public const int HistoryMax	= 10; // maximum number of backlogged feed items to store

	public static void Main(string[] args)
	{
		bool slowAtNight = false;
		
		if ( (args.Length < 3) || (args.Length > 4) )
		{
			Console.WriteLine("Twitterbot.exe <feed file> <minutes to sleep> <night time slowdown>");
			Console.WriteLine("Usage:");
			Console.WriteLine("mono Twitterbot.exe feeds.xml 30 yes");
			
			Console.WriteLine("");
			Console.WriteLine("Enabling night time slowdown will cause the Twitterbot to double its");
			Console.WriteLine("sleep interval between the hours of 12:00 a.m. and 6 a.m."); 
			return;
		}
	
		string feedFile = args[0];
		int sleep = Convert.ToInt32(args[1]);
		
		if ( (args[2] == "yes") || (args[2] == "y") )
			slowAtNight = true;
		
		List<Feed> feedList = new List<Feed>();
			
		Console.WriteLine("Reading {0} and looping {1} minutes", feedFile, sleep);
		
		/*
		 *	The following block is quite basic, open the feed xml file and
		 *	generate a List of Feed objects.
		 */
		////////////////////////////////////////////////////////////////////////
		XmlDocument feedXml = new XmlDocument();
		XmlNodeList feeds;
		FileStream stream;
		
		if (File.Exists(feedFile))
		{
			stream = File.OpenRead(feedFile);
		}
		else
		{
			Console.WriteLine("File {0} not found", feedFile);
			return;
		}

		feedXml.Load(stream);
		feeds = feedXml.GetElementsByTagName("feed");
		
		foreach (XmlNode feed in feeds)
		{
			string name = feed.Attributes["name"].InnerText;
			string url = feed.Attributes["url"].InnerText;
			string twitter = feed.Attributes["twitter"].InnerText;
			string pass = feed.Attributes["password"].InnerText;
			
			feedList.Add(new Feed(name, url, twitter, pass));
		}
		
		stream.Close();
		////////////////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////////////


		/* 
		 *	Our core runloop will iterate every 'sleep' minutes, first udpating
		 *	each one of our feeds, checking for duplicates, posting new entries
		 *	to the Feed's twitter account, and finally backlogging entries
		 */
		////////////////////////////////////////////////////////////////////////
		do
		{
			foreach (Feed feed in feedList)
			{
				Console.WriteLine("Reading {0}", feed.Name);
				try
				{
					feed.Rss = RssFeed.Read(feed.Url);
				}
				catch (Exception ex)
				{
					Console.WriteLine("RSS.NET received an exception!{0}{1}", 
						Environment.NewLine, ex);
					continue;
				}

				List<RssItem> tempList = new List<RssItem>();

				foreach (RssChannel channel in feed.Rss.Channels)
				{
					int count = 0;
					
					foreach (RssItem item in channel.Items)
					{			
						if (count == TwitterMax)
							break;
										
						if (feed.LastItems.Count > 0)
						{
							bool found = false;
							foreach (RssItem lastItem in feed.LastItems)
							{
								if (item.Title == lastItem.Title)
								{
									found = true;
									break;
								}
							}
							
							if (!found)
							{
								tempList.Add(item);
							}
						}
						else
						{
							tempList.Add(item);
						}
						
						++count;
					}
					
					break; // on the off-chance there is more than one channel
				}

				foreach (RssItem item in tempList)
				{
					// Prepare to post to twitter
					string post = PrepareTwitterStatus(item);
					Console.WriteLine("[{0}] Preparing post: {1}", DateTime.Now, post);
					
					if (!PostToTwitter(feed, post))
					{
						Console.WriteLine("[{0}] Failed to post to twitter!", DateTime.Now);
					}

					// Patiently count to five to reduce spammage
					Thread.Sleep(5000);
				}
			
				// Add our added items to the backlog list
				feed.AddItems(tempList);
			}
			
			Console.WriteLine("--------");
		
			// If we're slowing down at night, check to see if we're between 12:00 and 6:00 exclusive
			if ( (slowAtNight) && (DateTime.Now.Hour >= 0) && (DateTime.Now.Hour < 6) )
			{
				int slowSleep = (sleep * 2);
				Console.WriteLine("Doubling interval of {0} minute(s) to {1} minute(s) for night time hours", sleep, slowSleep);
				Thread.Sleep((slowSleep * 1000)*60);
			}
			else			
			{
				Thread.Sleep((sleep * 1000)*60);
			}
		} while (true);
		////////////////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////////////
	}
	
	
	public static bool PostToTwitter(Feed feed, string post)
	{
		HttpWebRequest request = null;
		string returnString = string.Empty;			
		HttpWebResponse response = null;

		try
		{
			request = GenerateGetOrPostRequest(TwitterUrl, "POST", string.Format("status={0}", post), 
											feed.TwitterName, feed.TwitterPass);
			response = (HttpWebResponse)request.GetResponse();
			Stream responseStream = response.GetResponseStream( );
			StreamReader reader = new StreamReader(responseStream,Encoding.UTF8);

			try
			{
				returnString = reader.ReadToEnd( );
			}
			catch (Exception ex)
			{
				Console.WriteLine("Received exception! {0}{1}", Environment.NewLine, ex);
				return false;
			}
			finally
			{
				if (reader != null)
					reader.Close();
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("Received exception! {0}{1}", Environment.NewLine, ex);
			return false;
		}
		finally
		{
			if (response != null)
				response.Close();
		}		

		return true;
	}


	public static string PrepareTwitterStatus(RssItem item)
	{
		string postString;
		string titleString = string.Empty;
		bool tainted = false;
		
		// This wee little bit of information should ensure we don't add any &#39; shit into the string 
		foreach (char oneChar in item.Title.ToCharArray())
		{
			if ( (oneChar == ';') && (tainted) )
			{
				tainted = false;
				continue;
			}
			else if (oneChar == '&')
			{
				tainted = true;
				continue;
			}
			else if (oneChar == ';') 
			{
				continue;
			}
			else
			{
				if (!tainted)
					titleString += oneChar;
			}
		}
		
		if (item.Title.Length > 124)
		{
			postString = titleString.Substring(0,124);
			postString += ".. ";
		}
		else
		{
			postString = titleString;
		}

		Console.WriteLine(item.Link);
		postString += string.Format(": {0}", FetchTinyUrl(item.Link));
		
		return postString;
	}


	public static string FetchTinyUrl(Uri longUrl)
	{
		string tinyurl;
		string target_url = HttpUtility.UrlEncode(longUrl.ToString());

		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(string.Format("{0}{1}", TinyUrl, target_url));
		HttpWebResponse response = null;
		
		try
		{
			response = (HttpWebResponse)request.GetResponse();
            Stream responseStream = response.GetResponseStream( );
            StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);

            try
            {
                tinyurl = reader.ReadToEnd();
            }
            catch (Exception ex)
            {
				Console.WriteLine("Received exception! {0}{1}", Environment.NewLine, ex);
				return string.Empty;
            }
            finally
            {
            	if (reader != null)
	                reader.Close();
            }
	    }
		catch (Exception ex)
		{
			Console.WriteLine("Received exception! {0}{1}", Environment.NewLine, ex);
			return string.Empty;
		}
	   finally
	   {
	   		if (response != null)
		       response.Close();
	   }
		
		JavaScriptSerializer serializer = new JavaScriptSerializer();
		Dictionary<string, string> results = serializer.Deserialize<Dictionary<string, string>>(tinyurl);
		return results["encoded"];
	}
	

	public static HttpWebRequest GenerateGetOrPostRequest(string uriString, string method, string postData, string user, string pass)
    {
		if ((method.ToUpper() != "GET") && (method.ToUpper() != "POST"))
			throw new ArgumentException(method + " is not a valid method.  Use GET or POST.","method");
	
		HttpWebRequest httpRequest = (HttpWebRequest)WebRequest.Create(uriString);
		httpRequest.Method = method;
		httpRequest.UserAgent = "Twitterbot";

		if (method.ToUpper() =="POST")
		{
			if ( (user != null) && (pass != null) )
			{
				httpRequest.Credentials = new NetworkCredential(user, pass);
			}

			// Get the bytes for the request, should be pre-escaped
			byte[] bytes = Encoding.UTF8.GetBytes(postData);

			// Set the content type of the data being posted.
			httpRequest.ContentType = "application/x-www-form-urlencoded";

			// Set the content length of the string being posted.
			httpRequest.ContentLength = bytes.Length;

			Stream requestStream = httpRequest.GetRequestStream();
			requestStream.Write(bytes,0,bytes.Length);
			// Done updating for POST so close the stream
			requestStream.Close();
		}
            
		return httpRequest;
    }

}

		
public class Feed
{
	public string Name;
	public string Url;
	public string TwitterName;
	public string TwitterPass;
	public RssFeed Rss;
	public List<RssItem> LastItems;

	public Feed(string name, string url, string twitter, string pass)
	{
		Name = name;
		Url = url;
		TwitterName = twitter;
		TwitterPass = pass;
		LastItems = new List<RssItem>(10);
	}
	
	public void AddItems(List<RssItem> items)
	{
		// If we've exceeded our max, remove items.Count from the front of the list
		// and add items to the end of the list
		if (LastItems.Count > Twitterbot.HistoryMax)
		{
			for (int i = 0; i < items.Count; ++i)
			{
				LastItems.RemoveAt(i);
			}
		}	
			
		LastItems.AddRange(items);
	}
}
