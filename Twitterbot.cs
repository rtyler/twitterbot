using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using Rss;

public class Twitterbot
{
	public const string TwitterUrl = "http://twitter.com/statuses/update.xml";
	public const string TinyUrl = "http://tinyurl.com/api-create.php?url=";
	public const int TwitterMax = 2; // maximum number of feed updates to pull

	public static void Main(string[] args)
	{
		
		if ( (args.Length < 3) || (args.Length > 4) )
		{
			Console.WriteLine("Twitterbot.exe FEEDFILE [--loop <minutes to sleep>]");
			return;
		}
	
		string feedFile = args[0];
		int sleep = Convert.ToInt32(args[2]);
		List<Feed> feedList = new List<Feed>();
			
		Console.WriteLine("Reading {0} and looping {1} minutes", feedFile, sleep);
		XmlDocument feedXml = new XmlDocument();
		XmlNodeList feeds;
		FileStream stream;
		
		if (File.Exists(feedFile))
		{
			stream = File.OpenRead(feedFile);
		}
		else
		{
			throw new Exception(String.Format("File {0} not found", feedFile));
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

		do
		{
			foreach (Feed feed in feedList)
			{
				Console.WriteLine("Reading {0}", feed.Name);
				feed.Rss = RssFeed.Read(feed.Url);

				foreach (RssChannel channel in feed.Rss.Channels)
				{
					List<RssItem> tempList = new List<RssItem>();
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
										found = true;
							}
							
							if (!found)
								tempList.Add(item);
							
						}
						else
						{
							tempList.Add(item);
						}
						
						++count;
					}
					
					feed.LastItems.Clear();
					feed.LastItems.AddRange(tempList);
					break; // on the off-chance there is more than one channel
				}

				foreach (RssItem item in feed.LastItems)
				{
					// Prepare to post to twitter
					string post = PrepareTwitterStatus(item);
					Console.WriteLine("Preparing post: {0}", post);
					
					if (!PostToTwitter(feed, post))
					{
						Console.WriteLine("Failed to post to twitter!");
					}

					// Patiently count to five to reduce spammage
					Thread.Sleep(5000);
				}
			}
			
			Console.WriteLine("--------");
		
			Thread.Sleep((sleep * 1000)*60);
		} while (true);
	}
	
	public static bool PostToTwitter(Feed feed, string post)
	{
		HttpWebRequest request = GenerateGetOrPostRequest(TwitterUrl, "POST", string.Format("status={0}", post), 
											feed.TwitterName, feed.TwitterPass);
		string returnString = string.Empty;			
		HttpWebResponse response = null;

		try
		{
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

		postString += string.Format(": {0}", FetchTinyUrl(item.Link));
		
		return postString;
	}

	public static string FetchTinyUrl(Uri longUrl)
	{
		string tinyurl;

		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(string.Format("{0}{1}", TinyUrl, longUrl.ToString()));
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
		
		return tinyurl;
	}

	public static HttpWebRequest GenerateGetOrPostRequest(string uriString, string method, string postData, string user, string pass)
    {
		if ((method.ToUpper() != "GET") && (method.ToUpper() != "POST"))
			throw new ArgumentException(method + " is not a valid method.  Use GET or POST.","method");
	
		HttpWebRequest httpRequest = (HttpWebRequest)WebRequest.Create(uriString);
		httpRequest.Method = method;

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
}
