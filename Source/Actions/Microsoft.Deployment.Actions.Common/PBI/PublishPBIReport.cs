﻿using System.ComponentModel.Composition;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Deployment.Common.ActionModel;
using Microsoft.Deployment.Common.Actions;
using Microsoft.Deployment.Common.Helpers;
using Microsoft.Deployment.Common.Model.PBI;

namespace Microsoft.Deployment.Actions.Common.PBI
{
    [Export(typeof(IAction))]
    public class PublishPBIReport : BaseAction
    {
        private const int MAXIMUM_IMPORT_STATUS_ATTEMPTS = 92;
        private const string PBI_IMPORT_STATUS_URI = "beta/myorg/{0}imports/{1}";
        private const string PBI_IMPORT_URI = "beta/myorg/{0}imports/?datasetDisplayName={1}&nameConflict=Abort";

        public override async Task<ActionResponse> ExecuteActionAsync(ActionRequest request)
        {
            AzureHttpClient client = new AzureHttpClient(request.DataStore.GetJson("PBIToken", "access_token"));
            string pbiClusterUri = request.DataStore.GetValue("PBIClusterUri");
            string pbiWorkspaceId = request.DataStore.GetValue("PBIWorkspaceId");
            string pbixLocation = request.DataStore.GetValue("PBIXLocation");

            pbiWorkspaceId = string.IsNullOrEmpty(pbiWorkspaceId) ? string.Empty : "groups/" + pbiWorkspaceId + "/";

            byte[] file = null;
            using (WebClient wc = new WebClient())
            {
                file = wc.DownloadData(pbixLocation);
            }

            string filename = request.Info.AppName + RandomGenerator.GetDateStamp() + ".pbix";

            PBIImport pbiImport = JsonUtility.Deserialize<PBIImport>(await client.Request(pbiClusterUri + string.Format(PBI_IMPORT_URI, pbiWorkspaceId, filename), file, filename));

            PBIImportStatus pbiImportStatus = null;
            int attempts = 0;
            bool isImportInProgress = true;
            while (isImportInProgress && attempts < MAXIMUM_IMPORT_STATUS_ATTEMPTS)
            {
                pbiImportStatus = JsonUtility.Deserialize<PBIImportStatus>(await client.Request(HttpMethod.Get, string.Format(PBI_IMPORT_STATUS_URI, pbiWorkspaceId, pbiImport.Id)));
                switch (pbiImportStatus.ImportState)
                {
                    case "Publishing":
                        Thread.Sleep(5000);
                        break;
                    case "Succeeded":
                        isImportInProgress = false;
                        break;
                    default:
                        isImportInProgress = false;
                        break;
                }
                attempts++;
            }

            string reportUrl = pbiImportStatus == null || pbiImportStatus.Reports == null || pbiImportStatus.Reports.Count == 0 ? string.Empty : pbiImportStatus.Reports[0].WebUrl;

            return new ActionResponse(ActionStatus.Success, reportUrl);
        }
    }
}