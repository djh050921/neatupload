// SimpleUploadState.cs created with MonoDevelop
// User: brettle at 6:09 PM 8/25/2008
//
// To change standard headers go to Edit->Preferences->Coding->Standard Headers
//

using System;
using System.Collections;

namespace Brettle.Web.NeatUpload
{
	[Serializable]
	public class UploadState : ICopyFromObject
	{
		public UploadState() { }
		
		internal UploadState(string postBackID)
		{
			_PostBackID = postBackID;
			// TODO: If contents of _ProcessingStateDict change, we need to call OnChanged();
			_Files.Changed += new EventHandler(Files_Changed);
		}

        public void CopyFrom(object source)
        {
            UploadState src = (UploadState)source;
            this._BytesPerSec = src._BytesPerSec;
            this._BytesRead = src._BytesRead;
            this._BytesTotal = src._BytesTotal;
            this._Failure = src._Failure;
            this._FileBytesRead = src._FileBytesRead;
            this._Files = src._Files;
            this._MultiRequestObject = src._MultiRequestObject;
            this._PostBackID = src._PostBackID;
            this._ProcessingStateDict = src._ProcessingStateDict;
            this._Rejection = src._Rejection;
            this._Status = src._Status;
            this.BytesReadAtLastMark = src.BytesReadAtLastMark;
            this.IsMerging = src.IsMerging;
            this.TimeOfFirstByte = src.TimeOfFirstByte;
            this.TimeOfLastMark = src.TimeOfLastMark;
            this.TimeOfLastMerge = src.TimeOfLastMerge;
            this.UploadStateAtLastMerge = src.UploadStateAtLastMerge;
        }

		private string _PostBackID;
		/// <value>
		/// The post-back ID of the upload.
		/// </value>
		public string PostBackID {
			get {
				return _PostBackID;
			}
		}

		private UploadStatus _Status;
		/// <value>
		/// The status of the upload.
		/// </value>
		public UploadStatus Status {
			get {
				return _Status;
			}
			set {
				if (value != _Status)
				{
					_Status = value;
                    if (_Status != UploadStatus.NormalInProgress && _Status != UploadStatus.ChunkedInProgress)
                    {
                        _BytesPerSec = (int)Math.Round(BytesRead / TimeElapsed.TotalSeconds);
                    }
					OnChanged();
				}
			}
		}

		private object _MultiRequestObject;
		/// <value>
		/// An object that is shared while processing multi-request uploads.
		/// </value>
		public object MultiRequestObject {
			get {
				return _MultiRequestObject;
			}
			set {
				_MultiRequestObject = value;
				OnChanged();
			}
		}

		private int _BytesPerSec;
		/// <summary>
		/// An estimate of the number of bytes received during the past second 
        /// while the upload is in progress.  When the upload is finished this is
        /// an average over the entire upload.
		/// </summary>
		/// <value>
        /// An estimate of the number of bytes received during the past second 
        /// while the upload is in progress.  When the upload is finished this is
        /// an average over the entire upload.
        /// </value>
		public int BytesPerSec {
			get {
				return _BytesPerSec;
			}
		}

		internal Hashtable _ProcessingStateDict = new Hashtable();
		/// <summary>
		/// Processing state objects associated with the upload, indexed by
		/// control UniqueID.
		/// </summary>
		public IDictionary ProcessingStateDict {
			get {
				return _ProcessingStateDict;
			}
		}

		internal UploadedFileCollection _Files = new UploadedFileCollection();
		/// <summary>
		/// A collection of the <see cref="UploadedFile"/> objects associated with the
		/// the upload.
		/// </summary>
		/// <value>
		/// A collection of the <see cref="UploadedFile"/> objects associated with the
		/// the upload.
		/// </value>
		public UploadedFileCollection Files {
			get {
				return _Files;
			}
		}

		private long _BytesRead;
		/// <summary>
		/// The total number of bytes received for the upload so far.
		/// </summary>
		/// <value>
		/// The total number of bytes received for the upload so far.
		/// </value>
		public long BytesRead {
			get {
				return _BytesRead;
			}
			set {
				DateTime now = DateTime.Now;
				if (now < TimeOfFirstByte)
					TimeOfFirstByte = now;
				if (now > TimeOfLastMark.AddSeconds(1))
				{
					_BytesPerSec = (int)((value - BytesReadAtLastMark) / (now - TimeOfLastMark).TotalSeconds);
					TimeOfLastMark = now;
					BytesReadAtLastMark = value;
				}
				if (value != _BytesRead)
				{
					_BytesRead = value;
					OnChanged();
				}
			}
		}

		private long _FileBytesRead;
		/// <summary>
		/// The number of file bytes received for the upload.
		/// </summary>
		/// <value>
		/// The number of file bytes received for the upload.
		/// </value>
		/// <remarks>Only bytes that are part of file fields that the module processes
		/// are included in this count.</remarks>
		public long FileBytesRead {
			get {
				return _FileBytesRead;
			}
			set {
				if (value != _FileBytesRead)
				{
					_FileBytesRead = value;
					OnChanged();
				}
			}
		}

		private long _BytesTotal;
		/// <summary>
		/// The total number of bytes expected for the uploaded.
		/// </summary>
		/// <value>
		/// The total number of bytes expected for the uploaded.
		/// </value>
		public long BytesTotal {
			get {
				return _BytesTotal;
			}
			set {
				if (value != _BytesTotal)
				{
					_BytesTotal = value;
					OnChanged();
				}
			}
		}

		/// <summary>
		/// The time since the upload started.
		/// </summary>
		/// <value>
		/// The time since the upload started.
		/// </value>
		public TimeSpan TimeElapsed {
			get {
				return DateTime.Now - TimeOfFirstByte;
			}
		}

		private UploadException _Rejection;
		/// <summary>
		/// If an <see cref="UploadException"/> (or subclass) was thrown while
		/// processing the upload, that exception.  Otherwise, null.
		/// </summary>
		/// <value>
		/// If an <see cref="UploadException"/> (or subclass) was thrown while
		/// processing the upload, that exception.  Otherwise, null.
		/// </value>
		public UploadException Rejection {
			get {
				return _Rejection;
			}
			set {
				if (value != _Rejection)
				{
					_Rejection = value;
					OnChanged();
				}
			}
		}

		private Exception _Failure;
		/// <summary>
		/// If an exception that was not an <see cref="UploadException"/> 
		/// while processing the upload, that exception.  Otherwise, null.
		/// </summary>
		/// <value>
		/// If an exception that was not an <see cref="UploadException"/> 
		/// while processing the upload, that exception.  Otherwise, null.
		/// </value>
		public Exception Failure {
			get {
				return _Failure;
			}
			set {
				if (value != _Failure)
				{
					_Failure = value;
					OnChanged();
				}
			}
		}

		private void Files_Changed(object sender, EventArgs args)
		{
			OnChanged();
		}

		private void OnChanged()
		{
			if (!IsMerging && Changed != null)
				Changed(this, null);
		}

		internal void OnMerged()
		{
			UploadStateAtLastMerge = (UploadState)MemberwiseClone();
			UploadStateAtLastMerge.UploadStateAtLastMerge = null;
		}	

		internal event EventHandler Changed;
		internal DateTime TimeOfLastMerge = DateTime.MinValue;
		internal UploadState UploadStateAtLastMerge;
		private long BytesReadAtLastMark;
		private DateTime TimeOfLastMark = DateTime.MinValue;
		private DateTime TimeOfFirstByte = DateTime.MaxValue;
		internal bool DeleteAfterDelayWhenNotOpenReadWrite = false;
		internal bool IsMerging = false;
	}
}
