/*
 
NeatUpload - an HttpModule and User Controls for uploading large files
Copyright (C) 2005  Dean Brettle

This library is free software; you can redistribute it and/or
modify it under the terms of the GNU Lesser General Public
License as published by the Free Software Foundation; either
version 2.1 of the License, or (at your option) any later version.

This library is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
Lesser General Public License for more details.

You should have received a copy of the GNU Lesser General Public
License along with this library; if not, write to the Free Software
Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
*/

using System;
using System.IO;
using System.Web;
using System.Web.SessionState;
using System.Collections;
using System.Collections.Specialized;
using System.Net;

namespace Brettle.Web.NeatUpload
{
	[Serializable]
	public class UploadContext
	{
		// Create a logger for use in this class
		private static readonly log4net.ILog log 
			= log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		internal const string ContextItemKeyPrefix = "NeatUpload.UploadedFile-";
				
		// All NeatUpload InputFile controls will have name attributes starting with the following prefix
		// This will appear in form field values, query strings, and window names.
		// Note: Must not include a "." because it confuses IE's javascript) and must not include a "-" 
		// because we use that separate the postback id from the control id.
		internal const string NamePrefix = "NeatUpload_";
		
		// The hidden form fields that contain per-control StorageConfig info have names which start with:
		internal const string ConfigNamePrefix = "NeatUploadConfig_";
		
		// The hidden form field that contain the sizes files to expect
		internal const string FileSizesName = "NeatUploadFileSizes";

		internal static string NameToConfigName(string name)
		{
			if (!name.StartsWith(NamePrefix))
			{
				throw new ApplicationException(name + " does not start with " + NamePrefix);
			}
			
			return ConfigNamePrefix + name.Substring(NamePrefix.Length);
		}
		
		internal static UploadContext Current
		{
			get {
				HttpContext httpContext = HttpContext.Current;
				if (httpContext != null)
				{
					if (httpContext.Items["NeatUpload_UploadContext"] == null)
					{
						if (Config.Current.UseHttpModule)
						{
							FilteringWorkerRequest worker = UploadHttpModule.GetCurrentWorkerRequest() as FilteringWorkerRequest;
							if (worker != null)
							{
								httpContext.Items["NeatUpload_UploadContext"] = worker.GetUploadContext();
							}
						}
					}
					return (UploadContext)httpContext.Items["NeatUpload_UploadContext"];
				}
				return null;
			}
			set {
				HttpContext.Current.Items["NeatUpload_UploadContext"] = value;
			}
		}
		
		internal static UploadContext FindByID(string postBackID)
		{
			if (postBackID == null)
			{
				return null;
			}
			if (HttpContext.Current.Session == null)
			{
				return FindByIDInAppState(postBackID);
			}
			UploadContext uploadContext = HttpContext.Current.Session[UploadContext.NamePrefix + postBackID] as UploadContext;
			if (log.IsDebugEnabled) log.Debug("Session[" + UploadContext.NamePrefix + postBackID + "] = " + uploadContext);
			return uploadContext;
		}
		
		internal static UploadContext FindByIDInAppState(string postBackID)
		{
			UploadContext uploadContext = HttpContext.Current.Application[UploadContext.NamePrefix + postBackID] as UploadContext;
			if (log.IsDebugEnabled) log.Debug("Application[" + UploadContext.NamePrefix + postBackID + "] = " + uploadContext);
			if (uploadContext == null)
			{
				uploadContext = HttpContext.Current.Cache[UploadContext.NamePrefix + postBackID] as UploadContext;
			}
			return uploadContext;
		}
		
		internal UploadContext()
		{
			this.startTime = System.DateTime.Now;
			this.stopTime = System.DateTime.MaxValue;
			NeverSynced = true;
		}
		
		internal UploadedFileCollection Files = new UploadedFileCollection();
		
		internal long[] FileSizes = null;
		internal int NumAsyncFilesReceived = 0;

		private string postBackID;
		
		internal string PostBackID
		{
			get { lock(this) { return postBackID; } }
			set { lock(this) { postBackID = value; } }
		}
				
		internal void RegisterPostBack(string postBackID)
		{
			PostBackID = postBackID;
			
			// Add a reference to this UploadContext to the Application so that it can be accessed
			// by the ProgressBar in a separate request.
			if (HttpContext.Current != null)
			{
				if (HttpContext.Current.Session != null)
				{
					if (log.IsDebugEnabled) log.Debug("Storing UploadContext in Session[" + UploadContext.NamePrefix + PostBackID + "] for SessionID=" + HttpContext.Current.Session.SessionID);
					if (log.IsDebugEnabled) log.Debug("Storing UploadContext in Session[" + UploadContext.NamePrefix + PostBackID + "]");
					HttpContext.Current.Session[UploadContext.NamePrefix + PostBackID] = this;
				}
				else
				{
					if (log.IsDebugEnabled) log.Debug("Storing UploadContext in Application[" + UploadContext.NamePrefix + PostBackID + "]");
					HttpContext.Current.Application[UploadContext.NamePrefix + PostBackID] = this;
				}
			}
		}
				
				
		private bool NeverSynced = true;
		
		private bool _IsSessionAvailable = true;
		public bool IsSessionAvailable { get { lock(this) { return _IsSessionAvailable; } } } 
		
		internal void SyncWithSession(HttpSessionState session)
		{
			// If we don't know what the PostBackID is yet, then there is no way to find the
			// context in the session that we are suppose to sync with.  We fail silently in
			// this case because it can be hard for callers to know whether they have PostBackID
			// yet (since it can come from the query string, a hidden form field, or the name of
			// the file form field).  The important thing is to avoid setting NeverSynced = false,
			// because that would prevent future valid syncs from replacing old upload contexts
			// associated with cancelled uploads.
			if (PostBackID == null)
			{
				return;
			}
			if (session == null || session.IsReadOnly 
				|| session.Mode == System.Web.SessionState.SessionStateMode.Off)
			{
				lock (this)
				{
					_IsSessionAvailable = false;
					NeverSynced = false;
					return;
				}
			}
			UploadContext ctxInSession = FindByID(PostBackID);
			// If we've never synced this context to the session before then we ignore the
			// context in the session because it is from a previous upload attempt 
			// with the same postbackid.
			if (ctxInSession == null || NeverSynced)
			{
				ctxInSession = new UploadContext();
			}
			lock (ctxInSession)
			{
				if (ctxInSession.Status != UploadStatus.Cancelled)
				{
					ctxInSession.FileBytesRead = FileBytesRead;
					ctxInSession.SyncBytesRead = SyncBytesRead;
					ctxInSession.AsyncBytesRead = AsyncBytesRead;
					ctxInSession.SyncBytesTotal = SyncBytesTotal;
					ctxInSession.AsyncBytesTotal = AsyncBytesTotal;
					ctxInSession.SetContentLength(ContentLength);
					ctxInSession.Status = Status;
					ctxInSession.Exception = Exception;
					ctxInSession.StartTime = StartTime;
					ctxInSession.StopTime = StopTime;
					ctxInSession.CurrentFileName = CurrentFileName;
					ctxInSession.ProgressInfoByID = ProgressInfoByID;
					ctxInSession.Files = Files;
					ctxInSession.FileSizes = FileSizes;
					ctxInSession.NumAsyncFilesReceived = NumAsyncFilesReceived;
					ctxInSession.RegisterPostBack(PostBackID);
				}
				else
				{
					Status = ctxInSession.Status;
				}
				
				ctxInSession.NeverSynced = NeverSynced = false;
			}
		}
				
		internal void SyncFromSession(HttpSessionState session)
		{
			if (PostBackID == null)
			{
				throw new NullReferenceException("PostBackID");
			}
			if (session == null || session.Mode == System.Web.SessionState.SessionStateMode.Off)
			{
				return;
			}
			UploadContext ctxInSession = FindByID(PostBackID);
			if (ctxInSession == null)
			{
				return;
			}
			lock (ctxInSession)
			{
				FileBytesRead = ctxInSession.FileBytesRead;
				SyncBytesRead = ctxInSession.SyncBytesRead;
				AsyncBytesRead = ctxInSession.AsyncBytesRead;
				SyncBytesTotal = ctxInSession.SyncBytesTotal;
				AsyncBytesTotal = ctxInSession.AsyncBytesTotal;
				SetContentLength(ctxInSession.ContentLength);
				Status = ctxInSession.Status;
				Exception = ctxInSession.Exception;
				StartTime = ctxInSession.StartTime;
				StopTime = ctxInSession.StopTime;
				CurrentFileName = ctxInSession.CurrentFileName;
				ProgressInfoByID = ctxInSession.ProgressInfoByID;
				Files = ctxInSession.Files;
				FileSizes = ctxInSession.FileSizes;
				NumAsyncFilesReceived = ctxInSession.NumAsyncFilesReceived;
			}
		}
				
		internal UploadedFile CreateUploadedFile(string controlUniqueID, string fileName, string contentType, UploadStorageConfig storageConfig)
		{
			if (log.IsDebugEnabled) log.Debug("In CreateUploadedFile() controlUniqueID=" + controlUniqueID);
			UploadedFile uploadedFile 
				= UploadStorage.CreateUploadedFile(this, controlUniqueID, fileName, contentType, storageConfig);
			Files.Add(controlUniqueID, uploadedFile);
			
			if (fileName != null && fileName != string.Empty)
				CurrentFileName = fileName;
			
			return uploadedFile;
		}
		
		internal void CreateAspNetUploadedFile(string name)
		{
			Files.Add(name, new AspNetUploadedFile(name));
		}
		
		internal void CompleteRequest()
		{
			if (!IsAsyncRequest)
			{
				// Move the current UploadContext from the ApplicationState to the Cache so that we don't leak memory.
				// We only need it in the Cache briefly because it is only used by the inline ProgressBar to display
				// the status of the upload that just completed.
				HttpContext ctx = HttpContext.Current;
				if (ctx != null)
				{
					ctx.Cache.Insert(UploadContext.NamePrefix + PostBackID, this, null, 
									System.Web.Caching.Cache.NoAbsoluteExpiration, 
									TimeSpan.FromSeconds(60));
					if (ctx.Application[UploadContext.NamePrefix + PostBackID] != null)
					{
						ctx.Application.Remove(UploadContext.NamePrefix + PostBackID);
					}
				}

				if (Status != UploadStatus.Failed && Status != UploadStatus.Rejected)
				{
					Status = UploadStatus.Completed;
					UploadHttpModule.AccessSession(new SessionAccessCallback(this.SyncWithSession));
				}
			}
			else
			{
				// This async upload is complete, so decrement NumAsyncFilesRemaining and sync to session.
				NumAsyncFilesReceived++;
				UploadHttpModule.AccessSession(new SessionAccessCallback(this.SyncWithSession));
			}
		}
		
		internal double FractionComplete
		{
			get 
			{
				lock (this)
				{
					if (BytesTotal <= 0)
					{
						return 0;
					}
					return (double) BytesRead / BytesTotal; 
				}
			}
		}
		
		private DateTime startTime;
		internal DateTime StartTime
		{
			get { lock(this) { return startTime; } }
			set { lock(this) { startTime = value; } }
		}
		
		private DateTime stopTime;
		internal DateTime StopTime
		{
			get { lock(this) { return stopTime; } }
			set { lock(this) { stopTime = value; } }
		}
		
		private long fileBytesRead;
		internal long FileBytesRead
		{
			get { lock(this) { return fileBytesRead; } }
			set { lock(this) { fileBytesRead = value; } }
		}

		internal long BytesRead
		{
			get { lock(this) { return SyncBytesRead + AsyncBytesRead; } }
		}

		private long syncBytesRead;
		internal long SyncBytesRead
		{
			get { lock(this) { return syncBytesRead; } }
			set
			{
				lock(this)
				{
					syncBytesRead = value;
				}
			}
		}

		private long asyncBytesRead;
		internal long AsyncBytesRead
		{
			get { lock(this) { return asyncBytesRead; } }
			set
			{
				lock(this)
				{
					asyncBytesRead = value;
				}
			}
		}

		internal long BytesTotal
		{
			get { lock(this) { return SyncBytesTotal + AsyncBytesTotal; } }
		}

		private long syncBytesTotal;
		internal long SyncBytesTotal
		{
			get { lock(this) { return syncBytesTotal; } }
			set
			{
				lock(this)
				{
					syncBytesTotal = value;
				}
			}
		}

		private long asyncBytesTotal;
		internal long AsyncBytesTotal
		{
			get { lock(this) { return asyncBytesTotal; } }
			set
			{
				lock(this)
				{
					asyncBytesTotal = value;
				}
			}
		}

		private long contentLength;
		public long ContentLength
		{
			get { lock(this) { return contentLength; } }
		}

		internal void SetContentLength(long val)
		{
			lock(this)
			{
				contentLength = val;
			}
		}
		
		private UploadStatus status = UploadStatus.NormalInProgress;
		internal UploadStatus Status
		{
			get { lock(this) { return status; } }
			set
			{
				lock(this)
				{
					status = value;
					if (value != UploadStatus.NormalInProgress && value != UploadStatus.ChunkedInProgress)
					{
						StopTime = DateTime.Now;
					}
				}
			}
		}
 
 		private Exception serializableException = null;
 		
 		[NonSerialized]
		private Exception exception = null;
		internal Exception Exception
		{
			get 
			{
				lock(this) 
				{
					if (exception == null)
					{
						return serializableException;
					}
					return exception;
				}
			}
			set
			{
				lock(this)
				{
					exception = value;
					if (IsSerializable(exception))
					{
						serializableException = exception;
					}
					else
					{
						UploadException uploadException = exception as UploadException;
						if (uploadException != null)
						{
							serializableException = new UploadException(uploadException.HttpCode, uploadException.Message);
						}
						else
						{
							serializableException = new Exception(exception.Message);
						}
						serializableException.HelpLink = exception.HelpLink;
						serializableException.Source = exception.Source;
					}
				}
			}
		}
		
		private static bool IsSerializable(object obj)
		{
			if (obj == null)
				return true;
			try
			{
				System.IO.MemoryStream memoryStream = new System.IO.MemoryStream();
				System.Runtime.Serialization.Formatters.Binary.BinaryFormatter formatter 
					= new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
				formatter.Serialize(memoryStream, obj);
				memoryStream.Flush();
				memoryStream.Position = 0;
				formatter.Deserialize(memoryStream);
				return true;
			}
			catch (System.Runtime.Serialization.SerializationException)
			{
				return false;
			}
		}
		
		internal TimeSpan TimeRemaining
		{
			get
			{
				lock(this) 
				{
					if (BytesRead == 0 || BytesTotal < 0)
					{
						return TimeSpan.MaxValue;
					}
					else
					{
						double bytesRemaining = ((double)(BytesTotal - BytesRead));
						if (log.IsDebugEnabled) log.Debug("BytesRead = " + BytesRead + " bytesRemaining = " + bytesRemaining);
						double ticksRemaining = bytesRemaining * TimeElapsed.Ticks;
						return new TimeSpan((long)(ticksRemaining/BytesRead));
					}
				}
			}
		}
		
		internal TimeSpan TimeElapsed
		{
			get {
				lock(this) 
				{
					if (StopTime == DateTime.MaxValue)
						return DateTime.Now - StartTime;
					else
						return StopTime - StartTime;
				}
			}
		}
		
		private string currentFileName = "";
		internal string CurrentFileName
		{
			get { lock(this) { return currentFileName; } }
			set { lock(this) { currentFileName = value; } }
		}
		
		private long BytesReadAtLastRateUpdate;
		private DateTime TimeOfLastRateUpdate = DateTime.MinValue;
		private int _BytesPerSec = 0;
		internal int BytesPerSec
		{
			get
			{
				if (TimeOfLastRateUpdate == DateTime.MinValue)
				{
					TimeOfLastRateUpdate = StartTime;
				}
				TimeSpan timeSinceLastUpdate = DateTime.Now - TimeOfLastRateUpdate;
				// If we're done or we're just starting, use the average rate for all bytes read so far and pretend
				// at least 1 sec has elapsed to ensure that we don't get outrageous rates.
				if ((Status != UploadStatus.NormalInProgress && Status != UploadStatus.ChunkedInProgress)
				    || (TimeOfLastRateUpdate == StartTime && timeSinceLastUpdate < TimeSpan.FromSeconds(1)))
				{
					return (int)Math.Round(BytesRead / Math.Max(TimeElapsed.TotalSeconds, 1.0));
				}
				// Otherwise, keep track of the number bytes read over the last second or so.
				if (timeSinceLastUpdate > TimeSpan.FromSeconds(1))
				{
					_BytesPerSec
						= (int)Math.Round((BytesRead - BytesReadAtLastRateUpdate) / timeSinceLastUpdate.TotalSeconds);
					BytesReadAtLastRateUpdate = BytesRead;
					TimeOfLastRateUpdate = DateTime.Now;
				}
				return _BytesPerSec;
			}
		}
		
		internal Hashtable ProgressInfoByID = new Hashtable();
		
		internal bool IsAsyncRequest = false;
	}
}
