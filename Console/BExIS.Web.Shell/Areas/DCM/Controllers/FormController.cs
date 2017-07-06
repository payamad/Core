﻿using BExIS.Dcm.CreateDatasetWizard;
using BExIS.Dcm.Wizard;
using BExIS.Dlm.Entities.Common;
using BExIS.Dlm.Entities.MetadataStructure;
using BExIS.Dlm.Services.Administration;
using BExIS.Dlm.Services.Data;
using BExIS.Dlm.Services.MetadataStructure;
using BExIS.Dlm.Services.TypeSystem;
using BExIS.IO;
using BExIS.IO.Transform.Validation.Exceptions;
using BExIS.Modules.Dcm.UI.Models.CreateDataset;
using BExIS.Modules.Dcm.UI.Models.Metadata;
using BExIS.Security.Entities.Authorization;
using BExIS.Security.Entities.Subjects;
using BExIS.Security.Services.Authorization;
using BExIS.Utils.Data.MetadataStructure;
using BExIS.Xml.Helpers;
using BExIS.Xml.Helpers.Mapping;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using System.Xml.Linq;
using Vaiona.Utils.Cfg;
using Vaiona.Web.Extensions;
using Vaiona.Web.Mvc.Models;

namespace BExIS.Modules.Dcm.UI.Controllers
{
    public class FormController : Controller
    {
        private CreateTaskmanager TaskManager;

        #region Load Metadata formular actions

        public ActionResult EditMetadata(long datasetId, bool locked = false, bool created = false)
        {
            return RedirectToAction("LoadMetadata", "Form", new { entityId = datasetId, locked = false, created = false, fromEditMode = true });
        }

        public ActionResult ImportMetadata(long metadataStructureId, bool edit = true, bool created = false, bool locked = false)
        {
            ViewBag.Title = PresentationModel.GetViewTitleForTenant("Create Dataset", this.Session.GetTenant());

            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var newMetadata = (XmlDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

            var stepInfoModelHelpers = new List<StepModelHelper>();
            var Model = new MetadataEditorModel();

            TaskManager.AddToBus(CreateTaskmanager.METADATA_XML, XmlUtility.ToXDocument(newMetadata));

            AdvanceTaskManagerBasedOnExistingMetadata(metadataStructureId);

            foreach (var stepInfo in TaskManager.StepInfos)
            {
                var stepModelHelper = GetStepModelhelper(stepInfo.Id);

                if (stepModelHelper.Model == null)
                {
                    if (stepModelHelper.Usage is MetadataPackageUsage)
                    {
                        stepModelHelper.Model = CreatePackageModel(stepInfo.Id, false);
                        if (stepModelHelper.Model.StepInfo.IsInstanze)
                            LoadSimpleAttributesForModelFromXml(stepModelHelper);
                    }

                    if (stepModelHelper.Usage is MetadataNestedAttributeUsage)
                    {
                        stepModelHelper.Model = CreateCompoundModel(stepInfo.Id, false);
                        if (stepModelHelper.Model.StepInfo.IsInstanze)
                            LoadSimpleAttributesForModelFromXml(stepModelHelper);
                    }

                    getChildModelsHelper(stepModelHelper);
                }

                stepInfoModelHelpers.Add(stepModelHelper);
            }

            Model.StepModelHelpers = stepInfoModelHelpers;
            Model.Import = IsImportAvavilable(metadataStructureId);
            //set addtionaly functions
            Model.Actions = getAddtionalActions();
            Model.FromEditMode = edit;
            Model.Created = created;

            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.ENTITY_ID))
            {
                var entityId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.ENTITY_ID]);
                Model.EditRight = hasUserEditRights(entityId);
                Model.EditAccessRight = hasUserEditAccessRights(entityId);
                //Model.DatasetId = entityId;
            }
            else
            {
                Model.EditRight = false;
                Model.EditAccessRight = false;
                Model.DatasetId = -1;
            }

            ViewData["Locked"] = locked;

            return PartialView("MetadataEditor", Model);
        }

        public ActionResult LoadMetadata(long entityId, bool locked = false, bool created = false, bool fromEditMode = false, bool resetTaskManager = false, XmlDocument newMetadata = null)
        {
            var loadFromExternal = resetTaskManager;
            long metadataStructureId = -1;

            ViewBag.Title = PresentationModel.GetViewTitleForTenant("Create Dataset", this.Session.GetTenant());
            ViewData["Locked"] = locked;
            ViewData["ShowOptional"] = false;

            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            if (TaskManager == null)
            {
                TaskManager = new CreateTaskmanager();
                loadFromExternal = true;
            }

            var stepInfoModelHelpers = new List<StepModelHelper>();
            var Model = new MetadataEditorModel();

            stepInfoModelHelpers = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.ENTITY_TITLE))
            {
                if (TaskManager.Bus[CreateTaskmanager.ENTITY_TITLE] != null)
                    Model.DatasetTitle = TaskManager.Bus[CreateTaskmanager.ENTITY_TITLE].ToString();
            }
            else
                Model.DatasetTitle = "No Title available.";

            Model.DatasetId = entityId;
            Model.StepModelHelpers = stepInfoModelHelpers;
            Model.Created = created;

            //check if a metadatastructure has a import mapping
            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATASTRUCTURE_ID))
                metadataStructureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);

            if (metadataStructureId != -1)
                Model.Import = IsImportAvavilable(metadataStructureId);

            //FromCreateOrEditMode
            TaskManager.AddToBus(CreateTaskmanager.EDIT_MODE, fromEditMode);
            Model.FromEditMode = (bool)TaskManager.Bus[CreateTaskmanager.EDIT_MODE];

            Model.EditRight = hasUserEditRights(entityId);
            Model.EditAccessRight = hasUserEditAccessRights(entityId);

            //set addtionaly functions
            Model.Actions = getAddtionalActions();

            return PartialView("MetadataEditor", Model);
        }

        public ActionResult LoadMetadataFromExternal(long entityId, string title, long metadatastructureId, long datastructureId = -1, long researchplanId = -1, string sessionKeyForMetadata = "", bool resetTaskManager = false)
        {
            var loadFromExternal = true;
            long metadataStructureId = -1;

            //load metadata from session if exist
            var metadata = Session[sessionKeyForMetadata] != null
                ? (XmlDocument)Session[sessionKeyForMetadata]
                : new XmlDocument();

            ViewBag.Title = PresentationModel.GetViewTitleForTenant("Create Dataset", this.Session.GetTenant()); ;
            ViewData["Locked"] = true;
            ViewData["ShowOptional"] = false;

            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            if (TaskManager == null || resetTaskManager)
            {
                TaskManager = new CreateTaskmanager();
            }

            var stepInfoModelHelpers = new List<StepModelHelper>();
            var Model = new MetadataEditorModel();

            if (loadFromExternal)
            {
                var entityClassPath = "";
                //TaskManager = new CreateTaskmanager();
                Session["CreateDatasetTaskmanager"] = TaskManager;
                TaskManager.AddToBus(CreateTaskmanager.ENTITY_ID, entityId);

                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.ENTITY_CLASS_PATH))
                    entityClassPath = TaskManager.Bus[CreateTaskmanager.ENTITY_CLASS_PATH].ToString();

                var ready = true;

                // todo i case of entity "BExIS.Dlm.Entities.Data.Dataset" we need to have a check if the dataset is checked in later all enitities should support such functions over webapis
                if (entityClassPath.Equals("BExIS.Dlm.Entities.Data.Dataset"))
                {
                    var dm = new DatasetManager();
                    //todo need a check if entity is in use
                    if (!dm.IsDatasetCheckedIn(entityId))
                    {
                        ready = false;
                    }
                }

                if (ready)
                {
                    TaskManager.AddToBus(CreateTaskmanager.METADATASTRUCTURE_ID, metadatastructureId);
                    if (researchplanId != -1) TaskManager.AddToBus(CreateTaskmanager.RESEARCHPLAN_ID, researchplanId);
                    if (datastructureId != -1) TaskManager.AddToBus(CreateTaskmanager.DATASTRUCTURE_ID, datastructureId);

                    if (metadata != null && metadata.DocumentElement != null)
                        TaskManager.AddToBus(CreateTaskmanager.METADATA_XML, XmlUtility.ToXDocument(metadata));

                    TaskManager.AddToBus(CreateTaskmanager.ENTITY_TITLE, title);

                    var rpm = new ResearchPlanManager();
                    TaskManager.AddToBus(CreateTaskmanager.RESEARCHPLAN_ID, rpm.Repo.Get().First().Id);

                    AdvanceTaskManagerBasedOnExistingMetadata(metadatastructureId);
                    //AdvanceTaskManager(dsv.Dataset.MetadataStructure.Id);

                    foreach (var stepInfo in TaskManager.StepInfos)
                    {
                        var stepModelHelper = GetStepModelhelper(stepInfo.Id);

                        if (stepModelHelper.Model == null)
                        {
                            if (stepModelHelper.Usage is MetadataPackageUsage)
                            {
                                stepModelHelper.Model = CreatePackageModel(stepInfo.Id, false);
                                if (stepModelHelper.Model.StepInfo.IsInstanze)
                                    LoadSimpleAttributesForModelFromXml(stepModelHelper);
                            }

                            if (stepModelHelper.Usage is MetadataNestedAttributeUsage)
                            {
                                stepModelHelper.Model = CreateCompoundModel(stepInfo.Id, false);
                                if (stepModelHelper.Model.StepInfo.IsInstanze)
                                    LoadSimpleAttributesForModelFromXml(stepModelHelper);
                            }

                            getChildModelsHelper(stepModelHelper);
                        }

                        stepInfoModelHelpers.Add(stepModelHelper);
                    }

                    if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_XML))
                    {
                        var xMetadata = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

                        if (String.IsNullOrEmpty(title)) title = "No Title available.";

                        if (TaskManager.Bus.ContainsKey(CreateTaskmanager.ENTITY_TITLE))
                        {
                            if (TaskManager.Bus[CreateTaskmanager.ENTITY_TITLE] != null)
                                Model.DatasetTitle = TaskManager.Bus[CreateTaskmanager.ENTITY_TITLE].ToString();
                        }
                        else
                            Model.DatasetTitle = "No Title available.";
                    }
                }
                else
                {
                    ModelState.AddModelError(String.Empty, "Dataset is just in processing.");
                }
            }

            Model.DatasetId = entityId;
            Model.StepModelHelpers = stepInfoModelHelpers;
            Model.Created = false;

            //check if a metadatastructure has a import mapping
            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATASTRUCTURE_ID))
                metadataStructureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);

            if (metadataStructureId != -1)
                Model.Import = IsImportAvavilable(metadataStructureId);

            //FromCreateOrEditMode
            TaskManager.AddToBus(CreateTaskmanager.EDIT_MODE, false);
            Model.FromEditMode = (bool)TaskManager.Bus[CreateTaskmanager.EDIT_MODE];

            // set edit rights
            Model.EditRight = hasUserEditRights(entityId);
            Model.EditAccessRight = hasUserEditAccessRights(entityId);

            //set addtionaly functions
            Model.Actions = getAddtionalActions();

            return PartialView("MetadataEditor", Model);
        }

        public ActionResult ReloadMetadataEditor(bool locked = false, bool show = false)
        {
            ViewData["Locked"] = locked;
            ViewData["ShowOptional"] = show;

            ViewBag.Title = PresentationModel.GetViewTitleForTenant("Create Dataset", this.Session.GetTenant());
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var stepInfoModelHelpers = new List<StepModelHelper>();

            foreach (var stepInfo in TaskManager.StepInfos)
            {
                var stepModelHelper = GetStepModelhelper(stepInfo.Id);

                if (stepModelHelper.Model == null)
                {
                    if (stepModelHelper.Usage is MetadataPackageUsage)
                        stepModelHelper.Model = CreatePackageModel(stepInfo.Id, false);

                    if (stepModelHelper.Usage is MetadataNestedAttributeUsage)
                        stepModelHelper.Model = CreateCompoundModel(stepInfo.Id, false);

                    getChildModelsHelper(stepModelHelper);
                }

                stepInfoModelHelpers.Add(stepModelHelper);
            }

            var Model = new MetadataEditorModel();
            Model.StepModelHelpers = stepInfoModelHelpers;

            #region security permissions and authorisations check

            // set edit rigths
            // TODO: refactor

            bool hasAuthorizationRights = false;
            bool hasAuthenticationRigths = false;

            long entityId = -1;

            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.ENTITY_ID))
            {
                entityId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.ENTITY_ID]);
                Model.EditRight = hasUserEditRights(entityId);
                Model.EditAccessRight = hasUserEditAccessRights(entityId);
            }
            else
            {
                Model.EditRight = false;
                Model.EditAccessRight = false;
            }

            //Model.FromEditMode = true;

            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATASTRUCTURE_ID))
            {
                long metadataStructureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);
                Model.Import = IsImportAvavilable(metadataStructureId);
            }

            #endregion security permissions and authorisations check

            //set addtionaly functions
            Model.Actions = getAddtionalActions();

            return PartialView("MetadataEditor", Model);
        }

        public ActionResult StartMetadataEditor()
        {
            ViewBag.Title = PresentationModel.GetViewTitleForTenant("Create Dataset", this.Session.GetTenant());
            ViewData["ShowOptional"] = false;

            var Model = new MetadataEditorModel();

            if (TaskManager == null) TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            if (TaskManager != null)
            {
                //load empty metadata xml if needed
                if (!TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_XML))
                {
                    CreateXml();
                }

                var loaded = false;
                //check if formsteps are loaded
                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.FORM_STEPS_LOADED))
                {
                    loaded = (bool)TaskManager.Bus[CreateTaskmanager.FORM_STEPS_LOADED];
                }

                // load form steps
                if (loaded == false && TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATASTRUCTURE_ID))
                {
                    var metadataStrutureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);
                    // generate all steps
                    // one step for each complex type  in the metadata structure
                    AdvanceTaskManager(metadataStrutureId);
                }

                var stepInfoModelHelpers = new List<StepModelHelper>();

                // foreach step and the childsteps... generate a stepModelhelper
                foreach (var stepInfo in TaskManager.StepInfos)
                {
                    var stepModelHelper = GetStepModelhelper(stepInfo.Id);

                    if (stepModelHelper.Model == null)
                    {
                        if (stepModelHelper.Usage is MetadataPackageUsage)
                            stepModelHelper.Model = CreatePackageModel(stepInfo.Id, false);

                        if (stepModelHelper.Usage is MetadataNestedAttributeUsage)
                            stepModelHelper.Model = CreateCompoundModel(stepInfo.Id, false);

                        getChildModelsHelper(stepModelHelper);
                    }

                    stepInfoModelHelpers.Add(stepModelHelper);
                }

                Model.StepModelHelpers = stepInfoModelHelpers;

                //set import
                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATASTRUCTURE_ID))
                {
                    var id = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);
                    Model.Import = IsImportAvavilable(id);
                }

                //set addtionaly functions
                Model.Actions = getAddtionalActions();
            }

            return View("MetadataEditor", Model);
        }

        public ActionResult SwitchVisibilityOfOptionalElements(bool show)
        {
            return RedirectToAction("ReloadMetadataEditor", new { locked = true, show = !show });
        }

        private Dictionary<string, ActionInfo> getAddtionalActions()
        {
            var TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            if (TaskManager.Actions.Any())
            {
                return TaskManager.Actions;
            }

            return new Dictionary<string, ActionInfo>();
        }

        #endregion Load Metadata formular actions

        #region Import Metadata From external XML

        public ActionResult LoadExternalXml()
        {
            var validationMessage = "";

            if (TaskManager == null) TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            if (TaskManager != null &&
                TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATASTRUCTURE_ID) &&
                TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_IMPORT_XML_FILEPATH))
            {
                //xml metadata for import
                var metadataForImportPath = (string)TaskManager.Bus[CreateTaskmanager.METADATA_IMPORT_XML_FILEPATH];

                if (FileHelper.FileExist(metadataForImportPath))
                {
                    var metadataForImport = new XmlDocument();
                    metadataForImport.Load(metadataForImportPath);

                    // metadataStructure ID
                    var metadataStructureId = (Int64)TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID];
                    var metadataStructureManager = new MetadataStructureManager();
                    var metadataStructrueName = metadataStructureManager.Repo.Get(metadataStructureId).Name;

                    // loadMapping file
                    var path_mappingFile = Path.Combine(AppConfiguration.GetModuleWorkspacePath("DIM"), XmlMetadataImportHelper.GetMappingFileName(metadataStructureId, TransmissionType.mappingFileImport, metadataStructrueName));

                    // XML mapper + mapping file
                    var xmlMapperManager = new XmlMapperManager(TransactionDirection.ExternToIntern);
                    xmlMapperManager.Load(path_mappingFile, "IDIV");

                    // generate intern metadata without internal attributes
                    var metadataResult = xmlMapperManager.Generate(metadataForImport, 1, true);

                    // generate intern template metadata xml with needed attribtes
                    var xmlMetadatWriter = new XmlMetadataWriter(BExIS.Xml.Helpers.XmlNodeMode.xPath);
                    var metadataXml = xmlMetadatWriter.CreateMetadataXml(metadataStructureId,
                        XmlUtility.ToXDocument(metadataResult));

                    var metadataXmlTemplate = XmlMetadataWriter.ToXmlDocument(metadataXml);

                    // set attributes FROM metadataXmlTemplate TO metadataResult
                    var completeMetadata = XmlMetadataImportHelper.FillInXmlValues(metadataResult,
                        metadataXmlTemplate);

                    TaskManager.AddToBus(CreateTaskmanager.METADATA_XML, completeMetadata);

                    //LoadMetadata(long datasetId, bool locked= false, bool created= false, bool fromEditMode = false, bool resetTaskManager = false, XmlDocument newMetadata=null)
                    return RedirectToAction("ImportMetadata", "Form",
                        new { metadataStructureId = metadataStructureId });
                }
            }

            return Content("Error Message :" + validationMessage);
        }

        /// <summary>
        /// Selected File store din the BUS
        /// </summary>
        /// <param name="SelectFileUploader"></param>
        /// <returns></returns>
        public ActionResult SelectFileProcess(HttpPostedFileBase SelectFileUploader)
        {
            if (TaskManager == null) TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            if (SelectFileUploader != null)
            {
                //data/datasets/1/1/
                var dataPath = AppConfiguration.DataPath; //Path.Combine(AppConfiguration.WorkspaceRootPath, "Data");
                var storepath = Path.Combine(dataPath, "Temp", GetUsernameOrDefault());

                // if folder not exist
                if (!Directory.Exists(storepath))
                {
                    Directory.CreateDirectory(storepath);
                }

                var path = Path.Combine(storepath, SelectFileUploader.FileName);

                SelectFileUploader.SaveAs(path);

                TaskManager.AddToBus(CreateTaskmanager.METADATA_IMPORT_XML_FILEPATH, path);
            }

            return Content("");
        }

        public ActionResult ValidateExternalXml()
        {
            var validationMessage = "";

            if (TaskManager == null) TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            if (TaskManager != null &&
                TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATASTRUCTURE_ID) &&
                TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_IMPORT_XML_FILEPATH))
            {
                //xml metadata for import
                var metadataForImportPath = (string)TaskManager.Bus[CreateTaskmanager.METADATA_IMPORT_XML_FILEPATH];

                if (FileHelper.FileExist(metadataForImportPath))
                {
                    var metadataForImport = new XmlDocument();
                    metadataForImport.Load(metadataForImportPath);

                    // metadataStructure DI
                    var metadataStructureId = (Int64)TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID];
                    var metadataStructureManager = new MetadataStructureManager();
                    var metadataStructrueName = metadataStructureManager.Repo.Get(metadataStructureId).Name;

                    // loadMapping file
                    var path_mappingFile = Path.Combine(AppConfiguration.GetModuleWorkspacePath("DIM"), XmlMetadataImportHelper.GetMappingFileName(metadataStructureId, TransmissionType.mappingFileImport, metadataStructrueName));

                    // XML mapper + mapping file
                    var xmlMapperManager = new XmlMapperManager(TransactionDirection.ExternToIntern);
                    xmlMapperManager.Load(path_mappingFile, "IDIV");

                    validationMessage = xmlMapperManager.Validate(metadataForImport);
                }
            }

            return Content(validationMessage);
        }

        #endregion Import Metadata From external XML

        #region Add and Remove and Activate

        public ActionResult ActivateComplexUsage(int id)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            //TaskManager.SetCurrent(TaskManager.Get(parentStepId));

            var stepModelHelper = GetStepModelhelper(id);
            //StepModelHelper parentStepModelHelper = GetStepModelhelper(parentStepId);

            var u = LoadUsage(stepModelHelper.Usage);

            var active = stepModelHelper.Activated ? false : true;
            stepModelHelper.Activated = active;
            //stepModelHelper.Parent.Activated = active;
            if (stepModelHelper.Parent != null)
            {
                var pStepModelHelper = GetStepModelhelper(stepModelHelper.Parent.StepId);
                if (pStepModelHelper != null)
                    pStepModelHelper.Activated = active;
            }
            // update stepmodel to dictionary
            //AddStepModelhelper(newStepModelhelper);

            //update stepModel to parentStepModel
            //for (int i = 0; i > parentStepModelHelper.Childrens.Count; i++)
            //{
            //    StepModelHelper tmp = parentStepModelHelper.Childrens.ElementAt(i);
            //    if (tmp.StepId.Equals(stepModelHelper.StepId)) tmp = stepModelHelper;
            //}

            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
            {
                return PartialView("_metadataCompoundAttributeUsageView", stepModelHelper);
            }

            if (u is MetadataPackageUsage)
            {
                return PartialView("_metadataCompoundAttributeUsageView", stepModelHelper);
            }

            return null;
        }

        public ActionResult ActivateComplexUsageInAChoice(int parentid, int id)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            //TaskManager.SetCurrent(TaskManager.Get(parentStepId));

            var stepModelHelper = GetStepModelhelper(id);
            //StepModelHelper parentStepModelHelper = GetStepModelhelper(parentStepId);

            var u = LoadUsage(stepModelHelper.Usage);

            var active = stepModelHelper.Activated ? false : true;
            stepModelHelper.Activated = active;
            stepModelHelper.Parent.Activated = active;

            var firstOrDefault = stepModelHelper.Childrens.FirstOrDefault();
            if (firstOrDefault != null)
                firstOrDefault.Activated = active;

            var pStepModelHelper = GetStepModelhelper(stepModelHelper.Parent.StepId);
            pStepModelHelper.Activated = active;

            // update stepmodel to dictionary
            //AddStepModelhelper(newStepModelhelper);

            //update stepModel to parentStepModel
            for (var i = 0; i < pStepModelHelper.Childrens.Count; i++)
            {
                var child = pStepModelHelper.Childrens.ElementAt(i);
                var childStepModelHelper = GetStepModelhelper(child.StepId);
                child.Activated = child.StepId.Equals(id);
                childStepModelHelper.Activated = child.StepId.Equals(id);

                var childOfChild = child.Childrens.FirstOrDefault();
                if (childOfChild != null)
                    childOfChild.Activated = child.StepId.Equals(id);
            }

            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
            {
                return PartialView("_metadataCompoundAttributeUsageView", pStepModelHelper);
            }

            if (u is MetadataPackageUsage)
            {
                return PartialView("_metadataCompoundAttributeUsageView", pStepModelHelper);
            }

            return null;
        }

        public ActionResult AddComplexUsage(int parentStepId, int number)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            //TaskManager.SetCurrent(TaskManager.Get(parentStepId));

            var metadataStructureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);
            var position = number + 1;

            var parentStepModelHelper = GetStepModelhelper(parentStepId);
            var u = LoadUsage(parentStepModelHelper.Usage);

            //Create new step
            var newStep = new StepInfo(MetadataStructureUsageHelper.GetNameOfType(u))
            {
                Id = TaskManager.GenerateStepId(),
                parentTitle = parentStepModelHelper.Model.StepInfo.title,
                Parent = parentStepModelHelper.Model.StepInfo,
                IsInstanze = true,
            };

            var xPath = parentStepModelHelper.XPath + "//" + MetadataStructureUsageHelper.GetNameOfType(u) + "[" + position + "]";

            // add to parent stepId
            parentStepModelHelper.Model.StepInfo.Children.Add(newStep);
            TaskManager.StepInfos.Add(newStep);

            // create Model
            AbstractMetadataStepModel model = null;

            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
            {
                model = MetadataCompoundAttributeModel.ConvertToModel(parentStepModelHelper.Usage, number);
                model.Number = position;

                ((MetadataCompoundAttributeModel)model).ConvertMetadataAttributeModels(LoadUsage(parentStepModelHelper.Usage), metadataStructureId, newStep.Id);

                //Update metadata xml
                //add step to metadataxml
                AddCompoundAttributeToXml(model.Source, model.Number, parentStepModelHelper.XPath);
            }

            if (u is MetadataPackageUsage)
            {
                model = MetadataPackageModel.Convert(parentStepModelHelper.Usage, number);
                model.Number = position;
                ((MetadataPackageModel)model).ConvertMetadataAttributeModels(LoadUsage(parentStepModelHelper.Usage), metadataStructureId, newStep.Id);

                //Update metadata xml
                //add step to metadataxml
                AddPackageToXml(model.Source, model.Number, parentStepModelHelper.XPath);
            }

            // create StepModel for new step
            var newStepModelhelper = new StepModelHelper
            {
                StepId = newStep.Id,
                Usage = parentStepModelHelper.Usage,
                Number = position,
                Model = model,
                XPath = xPath,
                Level = parentStepModelHelper.Level + 1,
                Activated = true
            };

            newStep.Children = GetChildrenSteps(u, newStep, xPath, newStepModelhelper);
            newStepModelhelper.Model.StepInfo = newStep;
            newStepModelhelper = getChildModelsHelper(newStepModelhelper);

            // add stepmodel to dictionary
            AddStepModelhelper(newStepModelhelper);

            //add stepModel to parentStepModel
            parentStepModelHelper.Childrens.Insert(newStepModelhelper.Number - 1, newStepModelhelper);

            //update childrens of the parent step based on number
            for (var i = 0; i < parentStepModelHelper.Childrens.Count; i++)
            {
                var smh = parentStepModelHelper.Childrens.ElementAt(i);
                smh.Number = i + 1;
            }

            // add step to parent and update title of steps
            //parentStepModelHelper.Model.StepInfo.Children.Insert(newStepModelhelper.Number - 1, newStep);
            for (var i = 0; i < parentStepModelHelper.Model.StepInfo.Children.Count; i++)
            {
                var si = parentStepModelHelper.Model.StepInfo.Children.ElementAt(i);
                si.title = (i + 1).ToString();
            }

            //// load InstanzB for parentmodel
            parentStepModelHelper.Model.ConvertInstance((XDocument)(TaskManager.Bus[CreateTaskmanager.METADATA_XML]), parentStepModelHelper.XPath);

            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
            {
                return PartialView("_metadataCompoundAttributeView", parentStepModelHelper);
            }

            if (u is MetadataPackageUsage)
            {
                return PartialView("_metadataCompoundAttributeView", parentStepModelHelper);
            }

            return null;
        }

        public ActionResult AddMetadataAttributeUsage(int id, int parentid, int number, int parentModelNumber, int parentStepId)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataStructureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);

            var list = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            var stepModelHelperParent = list.Where(s => s.StepId.Equals(parentStepId)).FirstOrDefault();

            var parentUsage = LoadUsage(stepModelHelperParent.Usage);
            var pNumber = stepModelHelperParent.Number;

            var metadataAttributeUsage = MetadataStructureUsageHelper.GetChildren(parentUsage).Where(u => u.Id.Equals(id)).FirstOrDefault();

            //BaseUsage metadataAttributeUsage = MetadataStructureUsageHelper.GetSimpleUsageById(parentUsage, id);

            var modelAttribute = MetadataAttributeModel.Convert(metadataAttributeUsage, parentUsage, metadataStructureId, parentModelNumber, stepModelHelperParent.StepId);
            modelAttribute.Number = ++number;

            var model = stepModelHelperParent.Model;

            Insert(modelAttribute, stepModelHelperParent, number);
            UpdateChildrens(stepModelHelperParent, modelAttribute.Source.Id);

            //addtoxml
            AddAttributeToXml(parentUsage, parentModelNumber, metadataAttributeUsage, number, stepModelHelperParent.XPath);

            model.ConvertInstance((XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML], stepModelHelperParent.XPath + "//" + metadataAttributeUsage.Label.Replace(" ", string.Empty));

            if (model != null)
            {
                if (model is MetadataPackageModel)
                {
                    return PartialView("_metadataPackageUsageView", stepModelHelperParent);
                }

                if (model is MetadataCompoundAttributeModel)
                {
                    return PartialView("_metadataCompoundAttributeUsageView", stepModelHelperParent);
                }
            }

            return null;
        }

        public ActionResult DownComplexUsage(int parentStepId, int number)
        {
            var newIndex = number;
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            TaskManager.SetCurrent(TaskManager.Get(parentStepId));

            var stepModelHelper = GetStepModelhelper(parentStepId);
            var u = LoadUsage(stepModelHelper.Usage);

            if (newIndex <= stepModelHelper.Childrens.Count - 1)
            {
                var xPathOfSelectedElement = stepModelHelper.XPath + "//" + MetadataStructureUsageHelper.GetNameOfType(stepModelHelper.Usage).Replace(" ", string.Empty) + "[" + number + "]";
                var destinationXPathElement = stepModelHelper.XPath + "//" + MetadataStructureUsageHelper.GetNameOfType(stepModelHelper.Usage).Replace(" ", string.Empty) + "[" + (number + 1) + "]";

                ChangeInXml(xPathOfSelectedElement, destinationXPathElement);

                if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
                {
                    CreateCompoundModel(TaskManager.Current().Id, true);
                }

                if (u is MetadataPackageUsage)
                {
                    stepModelHelper.Model = CreatePackageModel(TaskManager.Current().Id, true);
                }

                var selectedStepModelHelper = stepModelHelper.Childrens.ElementAt(number - 1);
                stepModelHelper.Childrens.Remove(selectedStepModelHelper);

                stepModelHelper.Childrens.Insert(newIndex, selectedStepModelHelper);

                //update childrens of the parent step based on number
                for (var i = 0; i < stepModelHelper.Childrens.Count; i++)
                {
                    var smh = stepModelHelper.Childrens.ElementAt(i);
                    smh.Number = i + 1;
                    smh.Model.Number = i + 1;
                }

                var selectedStepInfo = stepModelHelper.Model.StepInfo.Children.ElementAt(number - 1);
                stepModelHelper.Model.StepInfo.Children.Remove(selectedStepInfo);
                stepModelHelper.Model.StepInfo.Children.Insert(newIndex, selectedStepInfo);

                for (var i = 0; i < stepModelHelper.Model.StepInfo.Children.Count; i++)
                {
                    var si = stepModelHelper.Model.StepInfo.Children.ElementAt(i);
                    si.title = (i + 1).ToString();
                }

                stepModelHelper.Model.ConvertInstance((XDocument)(TaskManager.Bus[CreateTaskmanager.METADATA_XML]), stepModelHelper.XPath);
            }

            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
            {
                return PartialView("_metadataCompoundAttributeView", stepModelHelper);
            }
            else if (u is MetadataPackageUsage)
            {
                return PartialView("_metadataCompoundAttributeView", stepModelHelper);
            }

            return null;
        }

        public ActionResult DownMetadataAttributeUsage(object value, int id, int parentid, int number, int parentModelNumber, int parentStepId)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var list = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            var stepModelHelperParent = list.Where(s => s.StepId.Equals(parentStepId)).FirstOrDefault();

            Down(stepModelHelperParent, id, number);

            UpdateChildrens(stepModelHelperParent, id);

            var model = stepModelHelperParent.Model;

            if (model != null)
            {
                if (model is MetadataPackageModel)
                {
                    return PartialView("_metadataPackageUsageView", stepModelHelperParent);
                }

                if (model is MetadataCompoundAttributeModel)
                {
                    return PartialView("_metadataCompoundAttributeUsageView", stepModelHelperParent);
                }
            }

            return null;
        }

        public ActionResult RemoveComplexUsage(int parentStepId, int number)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            TaskManager.SetCurrent(TaskManager.Get(parentStepId));

            var stepModelHelper = GetStepModelhelper(parentStepId);
            RemoveFromXml(stepModelHelper.XPath + "//" + MetadataStructureUsageHelper.GetNameOfType(stepModelHelper.Usage).Replace(" ", string.Empty) + "[" + number + "]");

            var u = LoadUsage(stepModelHelper.Usage);

            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
            {
                CreateCompoundModel(TaskManager.Current().Id, true);
            }

            if (u is MetadataPackageUsage)
            {
                stepModelHelper.Model = CreatePackageModel(TaskManager.Current().Id, true);
            }

            stepModelHelper.Childrens.RemoveAt(number - 1);

            //add stepModel to parentStepModel
            for (var i = 0; i < stepModelHelper.Childrens.Count; i++)
            {
                stepModelHelper.Childrens.ElementAt(i).Number = i + 1;
            }

            TaskManager.Remove(TaskManager.Current(), number - 1);

            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
            {
                return PartialView("_metadataCompoundAttributeView", stepModelHelper);
            }
            else if (u is MetadataPackageUsage)
            {
                return PartialView("_metadataCompoundAttributeView", stepModelHelper);
            }

            return null;
        }

        public ActionResult RemoveMetadataAttributeUsage(object value, int id, int parentid, int number, int parentModelNumber, int parentStepId)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var list = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            var stepModelHelperParent = list.Where(s => s.StepId.Equals(parentStepId)).FirstOrDefault();

            //find the right element in the list
            var removeAttributeModel = stepModelHelperParent.Model.MetadataAttributeModels.Where(m => m.Source.Id.Equals(id) && m.Number.Equals(number)).First();
            //remove attribute
            stepModelHelperParent.Model.MetadataAttributeModels.Remove(removeAttributeModel);
            //update childrenList
            UpdateChildrens(stepModelHelperParent, id);

            var model = stepModelHelperParent.Model;

            var parentUsage = stepModelHelperParent.Usage;
            var attrUsage = MetadataStructureUsageHelper.GetChildren(parentUsage).Where(u => u.Id.Equals(id)).FirstOrDefault();

            //remove from xml
            RemoveAttributeToXml(stepModelHelperParent.Usage, stepModelHelperParent.Number, attrUsage, number, MetadataStructureUsageHelper.GetNameOfType(attrUsage), stepModelHelperParent.XPath);

            if (model != null)
            {
                if (model is MetadataPackageModel)
                {
                    return PartialView("_metadataPackageUsageView", stepModelHelperParent);
                }

                if (model is MetadataCompoundAttributeModel)
                {
                    return PartialView("_metadataCompoundAttributeUsageView", stepModelHelperParent);
                }
            }

            return null;
        }

        public ActionResult UpComplexUsage(int parentStepId, int number)
        {
            var newIndex = number - 2;
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            TaskManager.SetCurrent(TaskManager.Get(parentStepId));

            var stepModelHelper = GetStepModelhelper(parentStepId);
            var u = LoadUsage(stepModelHelper.Usage);

            if (newIndex >= 0)
            {
                var xPathOfSelectedElement = stepModelHelper.XPath + "//" + MetadataStructureUsageHelper.GetNameOfType(stepModelHelper.Usage).Replace(" ", string.Empty) + "[" + number + "]";
                var destinationXPathElement = stepModelHelper.XPath + "//" + MetadataStructureUsageHelper.GetNameOfType(stepModelHelper.Usage).Replace(" ", string.Empty) + "[" + (number - 1) + "]";

                ChangeInXml(xPathOfSelectedElement, destinationXPathElement);

                if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
                {
                    CreateCompoundModel(TaskManager.Current().Id, true);
                }

                if (u is MetadataPackageUsage)
                {
                    stepModelHelper.Model = CreatePackageModel(TaskManager.Current().Id, true);
                }

                var selectedStepModelHelper = stepModelHelper.Childrens.ElementAt(number - 1);
                stepModelHelper.Childrens.Remove(selectedStepModelHelper);

                stepModelHelper.Childrens.Insert(newIndex, selectedStepModelHelper);

                //update childrens of the parent step based on number
                for (var i = 0; i < stepModelHelper.Childrens.Count; i++)
                {
                    var smh = stepModelHelper.Childrens.ElementAt(i);
                    smh.Number = i + 1;
                    smh.Model.Number = i + 1;
                }

                var selectedStepInfo = stepModelHelper.Model.StepInfo.Children.ElementAt(number - 1);
                stepModelHelper.Model.StepInfo.Children.Remove(selectedStepInfo);
                stepModelHelper.Model.StepInfo.Children.Insert(newIndex, selectedStepInfo);

                for (var i = 0; i < stepModelHelper.Model.StepInfo.Children.Count; i++)
                {
                    var si = stepModelHelper.Model.StepInfo.Children.ElementAt(i);
                    si.title = (i + 1).ToString();
                }

                stepModelHelper.Model.ConvertInstance((XDocument)(TaskManager.Bus[CreateTaskmanager.METADATA_XML]), stepModelHelper.XPath);
            }

            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
            {
                return PartialView("_metadataCompoundAttributeView", stepModelHelper);
            }
            else if (u is MetadataPackageUsage)
            {
                return PartialView("_metadataCompoundAttributeView", stepModelHelper);
            }

            return null;
        }

        public ActionResult UpMetadataAttributeUsage(object value, int id, int parentid, int number, int parentModelNumber, int parentStepId)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var list = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            var stepModelHelperParent = list.Where(s => s.StepId.Equals(parentStepId)).FirstOrDefault();

            // up in xml
            //ChangeInXml(stepModelHelperParent.Usage, parentModelNumber, id, number);

            // up in Model
            Up(stepModelHelperParent, id, number);

            UpdateChildrens(stepModelHelperParent, id);

            var model = stepModelHelperParent.Model;

            if (model != null)
            {
                if (model is MetadataPackageModel)
                {
                    return PartialView("_metadataPackageUsageView", stepModelHelperParent);
                }

                if (model is MetadataCompoundAttributeModel)
                {
                    return PartialView("_metadataCompoundAttributeUsageView", stepModelHelperParent);
                }
            }

            return null;
        }

        #endregion Add and Remove and Activate

        #region Load & Update advanced steps

        private StepInfo AddStepsBasedOnUsage(BaseUsage usage, StepInfo current, string parentXpath, StepModelHelper parent)
        {
            // genertae action, controller base on usage
            var actionName = "";
            var childName = "";
            var min = usage.MinCardinality;

            if (usage is MetadataPackageUsage)
            {
                actionName = "SetMetadataPackageInstanze";
                childName = ((MetadataPackageUsage)usage).MetadataPackage.Name;
            }
            else
            {
                actionName = "SetMetadataCompoundAttributeInstanze";

                if (usage is MetadataNestedAttributeUsage)
                    childName = ((MetadataNestedAttributeUsage)usage).Member.Name;

                if (usage is MetadataAttributeUsage)
                    childName = ((MetadataAttributeUsage)usage).MetadataAttribute.Name;
            }

            var list = new List<StepInfo>();
            var stepHelperModelList = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_XML))
            {
                var xMetadata = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

                var x = new XElement("null");
                var elements = new List<XElement>();

                var keyValueDic = new Dictionary<string, string>();
                keyValueDic.Add("id", usage.Id.ToString());

                if (usage is MetadataPackageUsage)
                {
                    keyValueDic.Add("type", BExIS.Xml.Helpers.XmlNodeType.MetadataPackageUsage.ToString());
                    elements = XmlUtility.GetXElementsByAttribute(usage.Label, keyValueDic, xMetadata).ToList();
                }
                else
                {
                    keyValueDic.Add("type", BExIS.Xml.Helpers.XmlNodeType.MetadataAttributeUsage.ToString());
                    elements = XmlUtility.GetXElementsByAttribute(usage.Label, keyValueDic, xMetadata).ToList();
                }

                x = elements.FirstOrDefault();

                if (x != null && !x.Name.Equals("null"))
                {
                    var xelements = x.Elements();

                    if (xelements.Count() > 0)
                    {
                        var counter = 0;

                        foreach (var element in xelements)
                        {
                            counter++;
                            var title = counter.ToString(); //usage.Label+" (" + counter + ")";
                            var id = Convert.ToInt64((element.Attribute("roleId")).Value.ToString());

                            var s = new StepInfo(title)
                            {
                                Id = TaskManager.GenerateStepId(),
                                Parent = current,
                                IsInstanze = true,
                                HasContent = MetadataStructureUsageHelper.HasUsagesWithSimpleType(usage),
                            };

                            var xPath = parentXpath + "//" + childName.Replace(" ", string.Empty) + "[" + counter + "]";

                            if (TaskManager.Root.Children.Where(z => z.title.Equals(title)).Count() == 0)
                            {
                                var newStepModelHelper = new StepModelHelper(s.Id, counter, usage, xPath, parent);
                                stepHelperModelList.Add(newStepModelHelper);

                                s.Children = GetChildrenSteps(usage, s, xPath, newStepModelHelper);

                                current.Children.Add(s);
                            }
                        }
                    }
                }

                //TaskManager.AddToBus(CreateDatasetTaskmanager.METADATAPACKAGE_IDS, MetadataPackageDic);
            }
            return current;
        }

        private List<StepInfo> GetChildrenSteps(BaseUsage usage, StepInfo parent, string parentXpath, StepModelHelper parentStepModelHelper)
        {
            var childrenSteps = new List<StepInfo>();
            var childrenUsages = MetadataStructureUsageHelper.GetCompoundChildrens(usage);
            var stepHelperModelList = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            if (childrenUsages.Count > 0)
            {
                foreach (var u in childrenUsages)
                {
                    var xPath = parentXpath + "//" + u.Label.Replace(" ", string.Empty) + "[1]";

                    var complex = false;

                    var actionName = "";
                    var attrName = "";

                    if (u is MetadataPackageUsage)
                    {
                        actionName = "SetMetadataPackage";
                    }
                    else
                    {
                        actionName = "SetMetadataCompoundAttribute";

                        if (u is MetadataAttributeUsage)
                        {
                            var mau = (MetadataAttributeUsage)u;
                            if (mau.MetadataAttribute.Self is MetadataCompoundAttribute)
                            {
                                complex = true;
                                attrName = mau.MetadataAttribute.Self.Name;
                            }
                        }

                        if (u is MetadataNestedAttributeUsage)
                        {
                            var mau = (MetadataNestedAttributeUsage)u;
                            if (mau.Member.Self is MetadataCompoundAttribute)
                            {
                                complex = true;
                                attrName = mau.Member.Self.Name;
                            }
                        }
                    }

                    if (complex)
                    {
                        var s = new StepInfo(u.Label)
                        {
                            Id = TaskManager.GenerateStepId(),
                            parentTitle = attrName,
                            Parent = parent,
                            IsInstanze = false,
                            GetActionInfo = new ActionInfo
                            {
                                ActionName = actionName,
                                ControllerName = "CreateSetMetadataPackage",
                                AreaName = "DCM"
                            },

                            PostActionInfo = new ActionInfo
                            {
                                ActionName = actionName,
                                ControllerName = "CreateSetMetadataPackage",
                                AreaName = "DCM"
                            }
                        };

                        if (TaskManager.StepInfos.Where(z => z.Id.Equals(s.Id)).Count() == 0)
                        {
                            var newStepModelHelper = new StepModelHelper(s.Id, 1, u, xPath,
                                parentStepModelHelper);
                            stepHelperModelList.Add(newStepModelHelper);

                            s = AddStepsBasedOnUsage(u, s, xPath, newStepModelHelper);
                            childrenSteps.Add(s);
                        }
                    }
                }
            }

            return childrenSteps;
        }

        private List<StepInfo> GetChildrenStepsFromMetadata(BaseUsage usage, StepInfo parent, string parentXpath, StepModelHelper parentStepModelHelper)
        {
            var childrenSteps = new List<StepInfo>();
            var childrenUsages = MetadataStructureUsageHelper.GetCompoundChildrens(usage);
            var stepHelperModelList = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            if (childrenUsages.Count > 0)
            {
                foreach (var u in childrenUsages)
                {
                    var number = 1;//childrenUsages.IndexOf(u) + 1;
                    var xPath = parentXpath + "//" + u.Label.Replace(" ", string.Empty) + "[" + number + "]";

                    var complex = false;

                    var actionName = "";
                    var attrName = "";

                    if (u is MetadataPackageUsage)
                    {
                        actionName = "SetMetadataPackage";
                    }
                    else
                    {
                        actionName = "SetMetadataCompoundAttribute";

                        if (u is MetadataAttributeUsage)
                        {
                            var mau = (MetadataAttributeUsage)u;
                            if (mau.MetadataAttribute.Self is MetadataCompoundAttribute)
                            {
                                complex = true;
                                attrName = mau.MetadataAttribute.Self.Name;
                            }
                        }

                        if (u is MetadataNestedAttributeUsage)
                        {
                            var mau = (MetadataNestedAttributeUsage)u;
                            if (mau.Member.Self is MetadataCompoundAttribute)
                            {
                                complex = true;
                                attrName = mau.Member.Self.Name;
                            }
                        }
                    }

                    if (complex)
                    {
                        var s = new StepInfo(u.Label)
                        {
                            Id = TaskManager.GenerateStepId(),
                            parentTitle = attrName,
                            Parent = parent,
                            IsInstanze = false,
                            GetActionInfo = new ActionInfo
                            {
                                ActionName = actionName,
                                ControllerName = "CreateSetMetadataPackage",
                                AreaName = "DCM"
                            },

                            PostActionInfo = new ActionInfo
                            {
                                ActionName = actionName,
                                ControllerName = "CreateSetMetadataPackage",
                                AreaName = "DCM"
                            }
                        };

                        //only not optional

                        if (TaskManager.StepInfos.Where(z => z.Id.Equals(s.Id)).Count() == 0)
                        {
                            var newStepModelHelper = new StepModelHelper(s.Id, 1, u, xPath,
                                parentStepModelHelper);
                            stepHelperModelList.Add(newStepModelHelper);
                            s = LoadStepsBasedOnUsage(u, s, xPath, newStepModelHelper);
                            childrenSteps.Add(s);
                        }
                    }
                    //}
                }
            }

            return childrenSteps;
        }

        private List<StepInfo> GetChildrenStepsUpdated(BaseUsage usage, StepInfo parent, string parentXpath)
        {
            var childrenSteps = new List<StepInfo>();
            var childrenUsages = MetadataStructureUsageHelper.GetCompoundChildrens(usage);
            var stepHelperModelList = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            foreach (var u in childrenUsages)
            {
                var label = u.Label.Replace(" ", string.Empty);
                var xPath = parentXpath + "//" + label + "[1]";
                var complex = false;

                var actionName = "";

                if (u is MetadataPackageUsage)
                {
                    actionName = "SetMetadataPackage";
                }
                else
                {
                    actionName = "SetMetadataCompoundAttribute";

                    if (u is MetadataAttributeUsage)
                    {
                        var mau = (MetadataAttributeUsage)u;
                        if (mau.MetadataAttribute.Self is MetadataCompoundAttribute)
                            complex = true;
                    }

                    if (u is MetadataNestedAttributeUsage)
                    {
                        var mau = (MetadataNestedAttributeUsage)u;
                        if (mau.Member.Self is MetadataCompoundAttribute)
                            complex = true;
                    }
                }

                if (complex)
                {
                    var s = new StepInfo(u.Label)
                    {
                        Id = TaskManager.GenerateStepId(),
                        Parent = parent,
                        IsInstanze = false,
                        //GetActionInfo = new ActionInfo
                        //{
                        //    ActionName = actionName,
                        //    ControllerName = "CreateSetMetadataPackage",
                        //    AreaName = "DCM"
                        //},

                        //PostActionInfo = new ActionInfo
                        //{
                        //    ActionName = actionName,
                        //    ControllerName = "CreateSetMetadataPackage",
                        //    AreaName = "DCM"
                        //}
                    };

                    s.Children = UpdateStepsBasedOnUsage(u, s, xPath).ToList();
                    childrenSteps.Add(s);

                    if (TaskManager.Root.Children.Where(z => z.title.Equals(s.title)).Count() == 0)
                    {
                        var p = GetStepModelhelper(parent.Id);
                        stepHelperModelList.Add(new StepModelHelper(s.Id, 1, u, xPath, p));
                    }
                }
            }

            return childrenSteps;
        }

        private StepInfo LoadStepsBasedOnUsage(BaseUsage usage, StepInfo current, string parentXpath, StepModelHelper parent)
        {
            // genertae action, controller base on usage
            var actionName = "";
            var childName = "";
            var min = usage.MinCardinality;

            if (usage is MetadataPackageUsage)
            {
                actionName = "SetMetadataPackageInstanze";
                childName = ((MetadataPackageUsage)usage).MetadataPackage.Name;
            }
            else
            {
                actionName = "SetMetadataCompoundAttributeInstanze";

                if (usage is MetadataNestedAttributeUsage)
                    childName = ((MetadataNestedAttributeUsage)usage).Member.Name;

                if (usage is MetadataAttributeUsage)
                    childName = ((MetadataAttributeUsage)usage).MetadataAttribute.Name;
            }

            var list = new List<StepInfo>();
            var stepHelperModelList = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_XML))
            {
                var xMetadata = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

                //var x = new XElement("null");
                var parentXElement = new XElement("tmp");

                var keyValueDic = new Dictionary<string, string>();
                keyValueDic.Add("id", usage.Id.ToString());

                if (usage is MetadataPackageUsage)
                {
                    keyValueDic.Add("type", BExIS.Xml.Helpers.XmlNodeType.MetadataPackageUsage.ToString());
                    //elements = XmlUtility.GetXElementsByAttribute(usage.Label, keyValueDic, xMetadata).ToList();
                    parentXElement = XmlUtility.GetXElementByXPath(parent.XPath, xMetadata);
                }
                else
                {
                    keyValueDic.Add("type", BExIS.Xml.Helpers.XmlNodeType.MetadataAttributeUsage.ToString());
                    //elements = XmlUtility.GetXElementsByAttribute(usage.Label, keyValueDic, xMetadata, parentXpath).ToList();
                    parentXElement = XmlUtility.GetXElementByXPath(parent.XPath, xMetadata);
                }

                //foreach (var x in elements)
                //{
                var x = parentXElement;

                if (x != null && !x.Name.Equals("null"))
                {
                    var xelements = x.Elements();

                    if (xelements.Count() > 0)
                    {
                        var counter = 0;

                        XElement last = null;

                        foreach (var element in xelements)
                        {
                            // if the last has not the same name reset count
                            if (last != null && !last.Name.Equals(element.Name))
                            {
                                counter = 0;
                            }

                            last = element;
                            counter++;
                            var title = counter.ToString(); //usage.Label+" (" + counter + ")";
                            var id = Convert.ToInt64((element.Attribute("roleId")).Value.ToString());

                            var s = new StepInfo(title)
                            {
                                Id = TaskManager.GenerateStepId(),
                                Parent = current,
                                IsInstanze = true,
                                HasContent = MetadataStructureUsageHelper.HasUsagesWithSimpleType(usage),

                                //GetActionInfo = new ActionInfo
                                //{
                                //    ActionName = actionName,
                                //    ControllerName = "CreateSetMetadataPackage",
                                //    AreaName = "DCM"
                                //},

                                //PostActionInfo = new ActionInfo
                                //{
                                //    ActionName = actionName,
                                //    ControllerName = "CreateSetMetadataPackage",
                                //    AreaName = "DCM"
                                //}
                            };

                            var xPath = parentXpath + "//" + childName.Replace(" ", string.Empty) + "[" + counter + "]";

                            if (TaskManager.Root.Children.Where(z => z.title.Equals(title)).Count() == 0)
                            {
                                var newStepModelHelper = new StepModelHelper(s.Id, counter, usage, xPath,
                                    parent);
                                stepHelperModelList.Add(newStepModelHelper);
                                s.Children = GetChildrenStepsFromMetadata(usage, s, xPath, newStepModelHelper);

                                current.Children.Add(s);
                            }
                        }
                    }
                }
                //}

                //TaskManager.AddToBus(CreateDatasetTaskmanager.METADATAPACKAGE_IDS, MetadataPackageDic);
            }
            return current;
        }

        private List<StepInfo> UpdateStepsBasedOnUsage(BaseUsage usage, StepInfo currentSelected, string parentXpath)
        {
            // genertae action, controller base on usage
            var actionName = "";
            var childName = "";
            if (usage is MetadataPackageUsage)
            {
                actionName = "SetMetadataPackageInstanze";
                childName = ((MetadataPackageUsage)usage).MetadataPackage.Name;
            }
            else
            {
                actionName = "SetMetadataCompoundAttributeInstanze";

                if (usage is MetadataNestedAttributeUsage)
                    childName = ((MetadataNestedAttributeUsage)usage).Member.Name;

                if (usage is MetadataAttributeUsage)
                    childName = ((MetadataAttributeUsage)usage).MetadataAttribute.Name;
            }

            var list = new List<StepInfo>();
            var stepHelperModelList = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];

            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_XML))
            {
                var xMetadata = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

                var x = XmlUtility.GetXElementByXPath(parentXpath, xMetadata);

                if (x != null && !x.Name.Equals("null"))
                {
                    var current = currentSelected;
                    var xelements = x.Elements();

                    if (xelements.Count() > 0)
                    {
                        var counter = 0;
                        foreach (var element in xelements)
                        {
                            counter++;
                            var title = counter.ToString();

                            if (current.Children.Where(s => s.title.Equals(title)).Count() == 0)
                            {
                                var id = Convert.ToInt64((element.Attribute("roleId")).Value.ToString());

                                var s = new StepInfo(title)
                                {
                                    Id = TaskManager.GenerateStepId(),
                                    Parent = current,
                                    IsInstanze = true,
                                    //GetActionInfo = new ActionInfo
                                    //{
                                    //    ActionName = actionName,
                                    //    ControllerName = "CreateSetMetadataPackage",
                                    //    AreaName = "DCM"
                                    //},

                                    //PostActionInfo = new ActionInfo
                                    //{
                                    //    ActionName = actionName,
                                    //    ControllerName = "CreateSetMetadataPackage",
                                    //    AreaName = "DCM"
                                    //}
                                };

                                var xPath = parentXpath + "//" + childName.Replace(" ", string.Empty) + "[" + counter + "]";

                                s.Children = GetChildrenStepsUpdated(usage, s, xPath);
                                list.Add(s);

                                if (TaskManager.Root.Children.Where(z => z.Id.Equals(s.Id)).Count() == 0)
                                {
                                    var parent = GetStepModelhelper(current.Id);
                                    var newStepModelHelper = new StepModelHelper(s.Id, counter, usage, xPath, parent);

                                    stepHelperModelList.Add(newStepModelHelper);
                                }
                            }//end if
                        }//end foreach
                    }//end if
                }
            }
            return list;
        }

        #endregion Load & Update advanced steps

        #region Helper

        // chekc if user exist
        // if true return usernamem otherwise "DEFAULT"
        public string GetUsernameOrDefault()
        {
            var username = string.Empty;
            try
            {
                username = HttpContext.User.Identity.Name;
            }
            catch { }

            return !string.IsNullOrWhiteSpace(username) ? username : "DEFAULT";
        }

        private StepModelHelper AddStepModelhelper(StepModelHelper stepModelHelper)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_STEP_MODEL_HELPER))
            {
                ((List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER]).Add(stepModelHelper);

                return stepModelHelper;
            }

            return stepModelHelper;
        }

        private void AdvanceTaskManager(long MetadataStructureId)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataStructureManager = new MetadataStructureManager();

            var metadataPackageList = metadataStructureManager.GetEffectivePackages(MetadataStructureId).ToList();

            var stepModelHelperList = new List<StepModelHelper>();
            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_STEP_MODEL_HELPER))
            {
                TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER] = stepModelHelperList;
            }
            else
            {
                TaskManager.Bus.Add(CreateTaskmanager.METADATA_STEP_MODEL_HELPER, stepModelHelperList);
            }

            TaskManager.StepInfos = new List<StepInfo>();

            foreach (var mpu in metadataPackageList)
            {
                //only add none optional usages
                var si = new StepInfo(mpu.Label)
                {
                    Id = TaskManager.GenerateStepId(),
                    parentTitle = mpu.MetadataPackage.Name,
                    Parent = TaskManager.Root,
                    IsInstanze = false,
                };

                TaskManager.StepInfos.Add(si);
                var stepModelHelper = new StepModelHelper(si.Id, 1, mpu, "Metadata//" + mpu.Label.Replace(" ", string.Empty) + "[1]", null);

                stepModelHelperList.Add(stepModelHelper);

                //if (mpu.MinCardinality > 0)
                //{
                si = AddStepsBasedOnUsage(mpu, si, "Metadata//" + mpu.Label.Replace(" ", string.Empty) + "[1]", stepModelHelper);
                TaskManager.Root.Children.Add(si);

                TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER] = stepModelHelperList;
                //}
            }

            TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER] = stepModelHelperList;
            Session["CreateDatasetTaskmanager"] = TaskManager;
        }

        private void AdvanceTaskManagerBasedOnExistingMetadata(long MetadataStructureId)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataStructureManager = new MetadataStructureManager();

            var metadataPackageList = metadataStructureManager.GetEffectivePackages(MetadataStructureId).ToList();

            var stepModelHelperList = new List<StepModelHelper>();
            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_STEP_MODEL_HELPER))
            {
                TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER] = stepModelHelperList;
            }
            else
            {
                TaskManager.Bus.Add(CreateTaskmanager.METADATA_STEP_MODEL_HELPER, stepModelHelperList);
            }

            TaskManager.StepInfos = new List<StepInfo>();

            foreach (var mpu in metadataPackageList)
            {
                //only add none optional usages
                var si = new StepInfo(mpu.Label)
                {
                    Id = TaskManager.GenerateStepId(),
                    parentTitle = mpu.MetadataPackage.Name,
                    Parent = TaskManager.Root,
                    IsInstanze = false,
                };

                TaskManager.StepInfos.Add(si);
                var stepModelHelper = new StepModelHelper(si.Id, 1, mpu, "Metadata//" + mpu.Label.Replace(" ", string.Empty) + "[1]", null);

                stepModelHelperList.Add(stepModelHelper);

                si = LoadStepsBasedOnUsage(mpu, si, "Metadata//" + mpu.Label.Replace(" ", string.Empty) + "[1]", stepModelHelper);
                TaskManager.Root.Children.Add(si);

                TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER] = stepModelHelperList;
            }

            TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER] = stepModelHelperList;
            Session["CreateDatasetTaskmanager"] = TaskManager;
        }

        private void CreateXml()
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            // load metadatastructure with all packages and attributes

            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATASTRUCTURE_ID))
            {
                var xmlMetadatWriter = new XmlMetadataWriter(XmlNodeMode.xPath);

                var metadataXml = xmlMetadatWriter.CreateMetadataXml(Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]));

                //local path
                //string path = Path.Combine(AppConfiguration.GetModuleWorkspacePath("DCM"), "metadataTemp.Xml");

                TaskManager.AddToBus(CreateTaskmanager.METADATA_XML, metadataXml);

                //setup loaded
                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.SETUP_LOADED))
                    TaskManager.Bus[CreateTaskmanager.SETUP_LOADED] = true;
                else
                    TaskManager.Bus.Add(CreateTaskmanager.SETUP_LOADED, true);

                //save
                //metadataXml.Save(path);
            }
        }

        private StepModelHelper getChildModelsHelper(StepModelHelper stepModelHelper)
        {
            if (stepModelHelper.Model.StepInfo.Children.Count > 0)
            {
                foreach (var childStep in stepModelHelper.Model.StepInfo.Children)
                {
                    var childStepModelHelper = GetStepModelhelper(childStep.Id);

                    if (childStepModelHelper.Model == null)
                    {
                        if (childStepModelHelper.Usage is MetadataPackageUsage)
                            childStepModelHelper.Model = CreatePackageModel(childStep.Id, false);

                        if (childStepModelHelper.Usage is MetadataNestedAttributeUsage)
                            childStepModelHelper.Model = CreateCompoundModel(childStep.Id, false);

                        if (childStepModelHelper.Usage is MetadataAttributeUsage)
                            childStepModelHelper.Model = CreateCompoundModel(childStep.Id, false);

                        if (childStepModelHelper.Model.StepInfo.IsInstanze)
                            LoadSimpleAttributesForModelFromXml(childStepModelHelper);
                    }

                    childStepModelHelper = getChildModelsHelper(childStepModelHelper);
                    stepModelHelper.Childrens.Add(childStepModelHelper);
                }
            }

            return stepModelHelper;
        }

        private List<BaseUsage> GetCompoundAttributeUsages(BaseUsage usage)
        {
            var list = new List<BaseUsage>();

            if (usage is MetadataPackageUsage)
            {
                var mpu = (MetadataPackageUsage)usage;

                foreach (var mau in mpu.MetadataPackage.MetadataAttributeUsages)
                {
                    list.AddRange(GetCompoundAttributeUsages(mau));
                }
            }

            if (usage is MetadataAttributeUsage)
            {
                var mau = (MetadataAttributeUsage)usage;

                if (mau.MetadataAttribute.Self is MetadataCompoundAttribute)
                {
                    list.Add(mau);

                    var mca = (MetadataCompoundAttribute)mau.MetadataAttribute.Self;

                    foreach (var mnau in mca.MetadataNestedAttributeUsages)
                    {
                        list.AddRange(GetCompoundAttributeUsages(mnau));
                    }
                }
            }

            if (usage is MetadataNestedAttributeUsage)
            {
                var mnau = (MetadataNestedAttributeUsage)usage;

                if (mnau.Member.Self is MetadataCompoundAttribute)
                {
                    list.Add(mnau);

                    var mca = (MetadataCompoundAttribute)mnau.Member.Self;

                    foreach (var m in mca.MetadataNestedAttributeUsages)
                    {
                        list.AddRange(GetCompoundAttributeUsages(m));
                    }
                }
            }

            return list;
        }

        private StepModelHelper GetStepModelhelper(int stepId)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_STEP_MODEL_HELPER))
            {
                return ((List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER]).Where(s => s.StepId.Equals(stepId)).FirstOrDefault();
            }

            return null;
        }

        private bool IsImportAvavilable(long metadataStructureId)
        {
            return XmlDatasetHelper.HasExportInformation(metadataStructureId);
        }

        private BaseUsage LoadUsage(BaseUsage usage)
        {
            if (usage is MetadataPackageUsage)
            {
                var msm = new MetadataStructureManager();
                return msm.PackageUsageRepo.Get(usage.Id);
            }

            if (usage is MetadataNestedAttributeUsage)
            {
                var mam = new MetadataAttributeManager();

                var x = from c in mam.MetadataCompoundAttributeRepo.Get()
                        from u in c.Self.MetadataNestedAttributeUsages
                        where u.Id == usage.Id //&& c.Id.Equals(parentId)
                        select u;

                return x.FirstOrDefault();
            }

            if (usage is MetadataAttributeUsage)
            {
                var mpm = new MetadataPackageManager();

                var q = from p in mpm.MetadataPackageRepo.Get()
                        from u in p.MetadataAttributeUsages
                        where u.Id == usage.Id // && p.Id.Equals(parentId)
                        select u;

                return q.FirstOrDefault();
            }

            return usage;
        }

        //    return stepModelHelper;
        //}
        private void setStepModelActive(StepModelHelper model)
        {
            model.Activated = true;
            if (model.Parent != null)
                setStepModelActive(model.Parent);
        }

        private void SetXml(XDocument metadataXml)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            // load metadatastructure with all packages and attributes

            if (metadataXml != null)
            {
                // locat path
                //string path = Path.Combine(AppConfiguration.GetModuleWorkspacePath("DCM"), "metadataTemp.Xml");

                TaskManager.AddToBus(CreateTaskmanager.METADATA_XML, metadataXml);

                //setup loaded
                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.SETUP_LOADED))
                    TaskManager.Bus[CreateTaskmanager.SETUP_LOADED] = true;
                else
                    TaskManager.Bus.Add(CreateTaskmanager.SETUP_LOADED, true);
            }
        }

        //private StepModelHelper GetStepModelhelper(long usageId, int number)
        //{
        //    TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
        //    if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_STEP_MODEL_HELPER))
        //    {
        //        return ((List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER]).Where(s => s.Usage.Id.Equals(usageId) && s.Number.Equals(number)).FirstOrDefault();
        //    }

        //    return null;
        //}

        //private int GetNumberOfUsageInStepModelHelper(long usageId)
        //{
        //    TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
        //    if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_STEP_MODEL_HELPER))
        //    {
        //        return ((List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER]).Where(s => s.Usage.Id.Equals(usageId)).Count() - 1;
        //    }

        //    return 0;
        //}

        //private void GenerateModelsForChildrens(StepModelHelper modelHelper, long metadataStructureId)
        //{
        //    foreach (StepModelHelper item in modelHelper.Childrens)
        //    {
        //        if (item.Childrens.Count() > 0)
        //        {
        //            GenerateModelsForChildrens(item, metadataStructureId);
        //        }

        //        if (item.Model == null)
        //        {
        //            BaseUsage u = LoadUsage(item.Usage);
        //            if (u is MetadataAttributeUsage || u is MetadataNestedAttributeUsage)
        //            {
        //                item.Model = MetadataCompoundAttributeModel.ConvertToModel(u, item.Number);
        //                ((MetadataCompoundAttributeModel)item.Model).ConvertMetadataAttributeModels(u, metadataStructureId, item.StepId);
        //            }

        //            if (u is MetadataPackageUsage)
        //            {
        //                item.Model = MetadataPackageModel.Convert(u, item.Number);
        //                ((MetadataPackageModel)item.Model).ConvertMetadataAttributeModels(u, metadataStructureId, item.StepId);
        //            }
        //        }
        //    }

        //}

        //private StepModelHelper GenerateModelsForChildrens(StepInfo stepInfo, long metadataStructureId)
        //{
        //    StepModelHelper stepModelHelper = GetStepModelhelper(stepInfo.Id);

        //    if (stepModelHelper.Model == null)
        //    {
        //        if (stepModelHelper.Usage is MetadataPackageUsage)
        //            stepModelHelper.Model = CreatePackageModel(stepInfo.Id, false);

        //        if (stepModelHelper.Usage is MetadataNestedAttributeUsage)
        //            stepModelHelper.Model = CreateCompoundModel(stepInfo.Id, false);

        //        getChildModelsHelper(stepModelHelper);
        //    }

        #endregion Helper

        #region Attribute

        /// <summary>
        /// Is called when the user write a letter in Autocomplete User Component
        /// </summary>
        [HttpPost]
        public ActionResult _AutoCompleteAjaxLoading(string text, string id)
        {
            // BUG: invalid call to ddm method
            // TODO: reimplement call to get auto-complete values
            //ISearchProvider provider = IoCFactory.Container.ResolveForSession<ISearchProvider>() as ISearchProvider;
            //return new JsonResult { Data = new SelectList(provider.GetTextBoxSearchValues(text, "all", "new", 10).SearchComponent.TextBoxSearchValues, "Value", "Name") };

            // WORKAROUND: return always an empty list
            return new JsonResult { Data = new SelectList(new List<string>(), "Value", "Name") };
        }

        private StepModelHelper Down(StepModelHelper stepModelHelperParent, long id, int number)
        {
            var list = stepModelHelperParent.Model.MetadataAttributeModels;

            var temp = list.Where(m => m.Id.Equals(id) && m.Number.Equals(number)).FirstOrDefault();
            var index = list.IndexOf(temp);

            list.RemoveAt(index);
            list.Insert(index + number, temp);

            return stepModelHelperParent;
        }

        private StepModelHelper DownComplexModel(StepModelHelper stepModelHelperParent, long id, int number)
        {
            var list = stepModelHelperParent.Childrens;

            return stepModelHelperParent;
        }

        /// <summary>
        /// insert at a spezific number in the same children usages
        /// </summary>
        /// <param name="childModel"></param>
        /// <param name="stepModelHelperParent"></param>
        /// <param name="number"></param>
        /// <returns></returns>
        private StepModelHelper Insert(MetadataAttributeModel childModel, StepModelHelper stepModelHelperParent, int number)
        {
            var childrensWithSameUsage = stepModelHelperParent.Model.MetadataAttributeModels.Where(m => m.Source.Id.Equals(childModel.Source.Id)).First();
            var indexOfFirstUsage = stepModelHelperParent.Model.MetadataAttributeModels.IndexOf(childrensWithSameUsage);

            stepModelHelperParent.Model.MetadataAttributeModels.Insert(indexOfFirstUsage + number - 1, childModel);

            return stepModelHelperParent;
        }

        private StepModelHelper Up(StepModelHelper stepModelHelperParent, long id, int number)
        {
            var list = stepModelHelperParent.Model.MetadataAttributeModels;

            var temp = list.FirstOrDefault(m => m.Id.Equals(id) && m.Number.Equals(number));
            var index = list.IndexOf(temp);

            list.RemoveAt(index);
            list.Insert(index - 1, temp);

            return stepModelHelperParent;
        }

        private StepModelHelper UpComplexModel(StepModelHelper stepModelHelperParent, long id, int number)
        {
            var list = stepModelHelperParent.Childrens;

            return stepModelHelperParent;
        }

        private StepModelHelper UpdateChildrens(StepModelHelper stepModelHelper)
        {
            var count = stepModelHelper.Model.MetadataAttributeModels.Count;

            for (var i = 0; i < count; i++)
            {
                var child = stepModelHelper.Model.MetadataAttributeModels.ElementAt(i);
                child.NumberOfSourceInPackage = count;
                child.Number = i + 1;
            }

            stepModelHelper.Model.MetadataAttributeModels = UpdateFirstAndLast(stepModelHelper.Model.MetadataAttributeModels);

            return stepModelHelper;
        }

        /// <summary>
        /// Update metadataattributemodels in a parentmodel.
        /// Update number of childrens, based on the usage id
        /// </summary>
        /// <param name="stepModelHelper"></param>
        /// <param name="usageId"></param>
        /// <returns></returns>
        private StepModelHelper UpdateChildrens(StepModelHelper stepModelHelper, long usageId)
        {
            var mams = stepModelHelper.Model.MetadataAttributeModels.Where(m => m.Source.Id.Equals(usageId));

            for (var i = 0; i < mams.Count(); i++)
            {
                var child = mams.ElementAt(i);
                child.NumberOfSourceInPackage = mams.Count();
                child.Number = i + 1;
            }

            mams = UpdateFirstAndLast(mams.ToList()).AsEnumerable();

            return stepModelHelper;
        }

        #endregion Attribute

        #region Xml

        #region Xml Add / Remove / Update

        private void AddAttributeToXml(BaseUsage parentUsage, int parentNumber, BaseUsage attribute, int number, string parentXPath)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);
            metadataXml = xmlMetadataWriter.AddAttribute(metadataXml, attribute, number, MetadataStructureUsageHelper.GetNameOfType(attribute), MetadataStructureUsageHelper.GetIdOfType(attribute).ToString(), parentXPath);

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;

            // locat path
            var path = Path.Combine(AppConfiguration.GetModuleWorkspacePath("DCM"), "metadataTemp.Xml");
            //metadataXml.Save
        }

        private void AddCompoundAttributeToXml(BaseUsage usage, int number, string xpath)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);

            metadataXml = xmlMetadataWriter.AddPackage(metadataXml, usage, number, MetadataStructureUsageHelper.GetNameOfType(usage), MetadataStructureUsageHelper.GetIdOfType(usage), MetadataStructureUsageHelper.GetChildren(usage), BExIS.Xml.Helpers.XmlNodeType.MetadataAttribute, BExIS.Xml.Helpers.XmlNodeType.MetadataAttributeUsage, xpath);

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;
        }

        private void AddPackageToXml(BaseUsage usage, int number, string xpath)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);

            metadataXml = xmlMetadataWriter.AddPackage(
                metadataXml,
                usage,
                number,
                MetadataStructureUsageHelper.GetNameOfType(usage),
                MetadataStructureUsageHelper.GetIdOfType(usage),
                MetadataStructureUsageHelper.GetChildren(usage),
                BExIS.Xml.Helpers.XmlNodeType.MetadataPackage,
                BExIS.Xml.Helpers.XmlNodeType.MetadataPackageUsage,
                xpath);

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;
        }

        private void ChangeInXml(string selectedXPath, string destinationXPath)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];
            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);

            metadataXml = xmlMetadataWriter.Change(metadataXml, selectedXPath, destinationXPath);

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;
            // locat path
            var path = Path.Combine(AppConfiguration.GetModuleWorkspacePath("DCM"), "metadataTemp.Xml");
            metadataXml.Save(path);
        }

        private void RemoveAttributeToXml(BaseUsage parentUsage, int packageNumber, BaseUsage attribute, int number, string metadataAttributeName, string parentXPath)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];
            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);

            metadataXml = xmlMetadataWriter.RemoveAttribute(metadataXml, attribute, number, metadataAttributeName, parentXPath);

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;
            // locat path
            //string path = Path.Combine(AppConfiguration.GetModuleWorkspacePath("DCM"), "metadataTemp.Xml");
            //metadataXml.Save
        }

        private void RemoveCompoundAttributeToXml(BaseUsage usage, int number)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);
            metadataXml = xmlMetadataWriter.RemovePackage(metadataXml, usage, number, MetadataStructureUsageHelper.GetNameOfType(usage));

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;
        }

        private void RemoveFromXml(string xpath)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);
            metadataXml = xmlMetadataWriter.Remove(metadataXml, xpath);

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;
        }

        private void UpdateAttribute(BaseUsage parentUsage, int packageNumber, BaseUsage attribute, int number, object value, string parentXpath)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];
            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);

            metadataXml = xmlMetadataWriter.Update(metadataXml, attribute, number, value, MetadataStructureUsageHelper.GetNameOfType(attribute), parentXpath);

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;
            // locat path
            var path = Path.Combine(AppConfiguration.GetModuleWorkspacePath("DCM"), "metadataTemp.Xml");
            metadataXml.Save(path);
        }

        private void UpdateCompoundAttributeToXml(BaseUsage usage, int number, string xpath)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            var metadataXml = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

            var xmlMetadataWriter = new XmlMetadataWriter(XmlNodeMode.xPath);

            metadataXml = xmlMetadataWriter.AddPackage(metadataXml, usage, number, MetadataStructureUsageHelper.GetNameOfType(usage), MetadataStructureUsageHelper.GetIdOfType(usage), MetadataStructureUsageHelper.GetChildren(usage), BExIS.Xml.Helpers.XmlNodeType.MetadataAttribute, BExIS.Xml.Helpers.XmlNodeType.MetadataAttributeUsage, xpath);

            TaskManager.Bus[CreateTaskmanager.METADATA_XML] = metadataXml;
        }

        #endregion Xml Add / Remove / Update

        #endregion Xml

        #region Validation

        public ActionResult Validate()
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];

            if (TaskManager != null && TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_STEP_MODEL_HELPER))
            {
                var stepInfoModelHelpers = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];
                ValidateModels(stepInfoModelHelpers.Where(s => s.Activated && s.IsParentActive()).ToList());
            }

            return RedirectToAction("ReloadMetadataEditor", "Form");
        }

        //XX number of index des values nötig
        [HttpPost]
        public ActionResult ValidateMetadataAttributeUsage(string value, int id, int parentid, string parentname, int number, int parentModelNumber, int parentStepId)
        {
            //delete all white spaces from start and end
            value = value.Trim();

            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var stepModelHelper = GetStepModelhelper(parentStepId);

            var ParentUsageId = stepModelHelper.Usage.Id;
            var parentUsage = LoadUsage(stepModelHelper.Usage);
            var pNumber = stepModelHelper.Number;

            var metadataAttributeUsage = MetadataStructureUsageHelper.GetChildren(parentUsage).Where(u => u.Id.Equals(id)).FirstOrDefault();

            //Path.Combine(AppConfiguration.GetModuleWorkspacePath("dcm"),"x","file.xml");

            //UpdateXml
            var metadataStructureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);
            var model = MetadataAttributeModel.Convert(metadataAttributeUsage, parentUsage, metadataStructureId, parentModelNumber, stepModelHelper.StepId);

            //check if datatype is a datetime then check display pattern and manipulate the incoming string
            if (model.SystemType.Equals(typeof(DateTime).Name))
            {
                if (!string.IsNullOrEmpty(model.DisplayPattern))
                {
                    var dt = DateTime.Parse(value);
                    value = dt.ToString(model.DisplayPattern);
                }
            }

            model.Value = value;
            model.Number = number;

            UpdateAttribute(parentUsage, parentModelNumber, metadataAttributeUsage, number, value, stepModelHelper.XPath);

            if (stepModelHelper.Model.MetadataAttributeModels.Where(a => a.Id.Equals(id) && a.Number.Equals(number)).Count() > 0)
            {
                var selectedMetadatAttributeModel = stepModelHelper.Model.MetadataAttributeModels.Where(a => a.Id.Equals(id) && a.Number.Equals(number)).FirstOrDefault();
                // select the attributeModel and change the value
                selectedMetadatAttributeModel.Value = model.Value;
                selectedMetadatAttributeModel.Errors = validateAttribute(selectedMetadatAttributeModel);
                return PartialView("_metadataAttributeView", selectedMetadatAttributeModel);
            }
            else
            {
                stepModelHelper.Model.MetadataAttributeModels.Add(model);
                return PartialView("_metadataAttributeView", model);
            }

            return null;
        }

        private List<Error> validateAttribute(MetadataAttributeModel aModel)
        {
            var errors = new List<Error>();
            //optional check
            if (aModel.MinCardinality > 0 && (aModel.Value == null || String.IsNullOrEmpty(aModel.Value.ToString())))
                errors.Add(new Error(ErrorType.MetadataAttribute, "is not optional", new object[] { aModel.DisplayName, aModel.Value, aModel.Number, aModel.ParentModelNumber, aModel.Parent.Label }));
            else
                if (aModel.MinCardinality > 0 && String.IsNullOrEmpty(aModel.Value.ToString()))
                errors.Add(new Error(ErrorType.MetadataAttribute, "is not optional", new object[] { aModel.DisplayName, aModel.Value, aModel.Number, aModel.ParentModelNumber, aModel.Parent.Label }));

            //check datatype
            if (aModel.Value != null && !String.IsNullOrEmpty(aModel.Value.ToString()))
            {
                if (!DataTypeUtility.IsTypeOf(aModel.Value, aModel.SystemType))
                {
                    errors.Add(new Error(ErrorType.MetadataAttribute, "Value can´t convert to the type: " + aModel.SystemType + ".", new object[] { aModel.DisplayName, aModel.Value, aModel.Number, aModel.ParentModelNumber, aModel.Parent.Label }));
                }
                else
                {
                    var type = Type.GetType("System." + aModel.SystemType);
                    var value = Convert.ChangeType(aModel.Value, type);

                    // check Constraints
                    foreach (var constraint in aModel.GetMetadataAttribute().Constraints)
                    {
                        if (value != null && !constraint.IsSatisfied(value))
                        {
                            errors.Add(new Error(ErrorType.MetadataAttribute, constraint.ErrorMessage, new object[] { aModel.DisplayName, aModel.Value, aModel.Number, aModel.ParentModelNumber, aModel.Parent.Label }));
                        }
                    }
                }
            }

            //// dataset title node should be check if its exit or not
            //if (errors.Count == 0 && aModel.DataType.ToLower().Contains("string"))
            //{
            //    XmlReader reader =;
            //}

            if (errors.Count == 0)
                return null;
            else
                return errors;
        }

        private void ValidateModels(List<StepModelHelper> stepModelHelpers)
        {
            foreach (var stepModeHelper in stepModelHelpers)
            {
                // if model exist then validate attributes
                if (stepModeHelper.Model != null)
                {
                    foreach (var metadataAttrModel in stepModeHelper.Model.MetadataAttributeModels)
                    {
                        metadataAttrModel.Errors = validateAttribute(metadataAttrModel);
                        //if (metadataAttrModel.Errors.Count > 0)
                        //    step.stepStatus = StepStatus.error;
                    }
                }
                // else check for required elements
                else
                {
                    stepModeHelper.Usage = LoadUsage(stepModeHelper.Usage);
                    if (MetadataStructureUsageHelper.HasUsagesWithSimpleType(stepModeHelper.Usage))
                    {
                        //foreach (var metadataAttrModel in stepModeHelper.Model.MetadataAttributeModels)
                        //{
                        //    metadataAttrModel.Errors = validateAttribute(metadataAttrModel);
                        //    if (metadataAttrModel.Errors.Count>0)
                        //        step.stepStatus = StepStatus.error;
                        //}

                        //if(MetadataStructureUsageHelper.HasRequiredSimpleTypes(stepModeHelper.Usage))
                        //{
                        //    StepInfo step = TaskManager.Get(stepModeHelper.StepId);
                        //    if (step != null && step.IsInstanze)
                        //    {
                        //        Error error = new Error(ErrorType.Other, String.Format("{0} : {1} {2}", "Step: ", stepModeHelper.Usage.Label, "is not valid. There are fields that are required and not yet completed are."));

                        //        errors.Add(new Tuple<StepInfo, List<Error>>(step, tempErrors));

                        //        step.stepStatus = StepStatus.error;
                        //    }
                        //}
                    }
                }
            }
        }

        private List<Error> validateStep(AbstractMetadataStepModel pModel)
        {
            var errorList = new List<Error>();

            if (pModel != null)
            {
                foreach (var m in pModel.MetadataAttributeModels)
                {
                    var temp = validateAttribute(m);
                    if (temp != null)
                        errorList.AddRange(temp);
                }
            }

            if (errorList.Count == 0)
                return null;
            else
                return errorList;
        }

        #endregion Validation

        #region Bus Functions

        private BaseUsage GetMetadataCompoundAttributeUsage(long id)
        {
            //return MetadataStructureUsageHelper.GetChildrenUsageById(Id);
            return MetadataStructureUsageHelper.GetMetadataAttributeUsageById(id);
        }

        private BaseUsage GetPackageUsage(long Id)
        {
            var mpm = new MetadataStructureManager();
            return mpm.PackageUsageRepo.Get(Id);
        }

        private long GetUsageId(int stepId)
        {
            if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_STEP_MODEL_HELPER))
            {
                var list = (List<StepModelHelper>)TaskManager.Bus[CreateTaskmanager.METADATA_STEP_MODEL_HELPER];
                return list.Where(s => s.StepId.Equals(stepId)).FirstOrDefault().Usage.Id;
            }

            return 0;
        }

        private List<MetadataAttributeModel> UpdateFirstAndLast(List<MetadataAttributeModel> list)
        {
            foreach (var x in list)
            {
                if (list.First().Equals(x)) x.first = true;
                else x.first = false;

                if (list.Last().Equals(x)) x.last = true;
                else x.last = false;
            }

            return list;
        }

        #endregion Bus Functions

        #region Models

        private MetadataCompoundAttributeModel CreateCompoundModel(int stepId, bool validateIt)
        {
            var stepInfo = TaskManager.Get(stepId);
            var stepModelHelper = GetStepModelhelper(stepId);

            var metadataStructureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);
            var Id = stepModelHelper.Usage.Id;

            var model = MetadataCompoundAttributeModel.ConvertToModel(stepModelHelper.Usage, stepModelHelper.Number);

            // get children
            model.ConvertMetadataAttributeModels(LoadUsage(stepModelHelper.Usage), metadataStructureId, stepInfo.Id);
            model.StepInfo = TaskManager.Get(stepId);

            if (stepInfo.IsInstanze == false)
            {
                //get Instance
                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_XML))
                {
                    var xMetadata = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];
                    model.ConvertInstance(xMetadata, stepModelHelper.XPath);
                }
            }
            else
            {
                if (stepModelHelper.Model != null)
                {
                    model = (MetadataCompoundAttributeModel)stepModelHelper.Model;
                }
                else
                {
                    stepModelHelper.Model = model;
                }
            }

            //if (validateIt)
            //{
            //    //validate packages
            //    List<Error> errors = validateStep(stepModelHelper.Model);
            //    if (errors != null)
            //        model.ErrorList = errors;
            //    else
            //        model.ErrorList = new List<Error>();

            //}

            model.StepInfo = stepInfo;

            return model;
        }

        private MetadataPackageModel CreatePackageModel(int stepId, bool validateIt)
        {
            var stepInfo = TaskManager.Get(stepId);
            var stepModelHelper = GetStepModelhelper(stepId);

            var metadataPackageId = stepModelHelper.Usage.Id;
            var metadataStructureId = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.METADATASTRUCTURE_ID]);

            var mdsManager = new MetadataStructureManager();
            var mdpManager = new MetadataPackageManager();
            var mpu = (MetadataPackageUsage)LoadUsage(stepModelHelper.Usage);
            var model = new MetadataPackageModel();

            model = MetadataPackageModel.Convert(mpu, stepModelHelper.Number);
            model.ConvertMetadataAttributeModels(mpu, metadataStructureId, stepId);

            if (stepInfo.IsInstanze == false)
            {
                //get Instance
                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.METADATA_XML))
                {
                    var xMetadata = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];
                    model.ConvertInstance(xMetadata, stepModelHelper.XPath);
                }
            }
            else
            {
                if (stepModelHelper.Model != null)
                {
                    model = (MetadataPackageModel)stepModelHelper.Model;
                }
                else
                {
                    stepModelHelper.Model = model;
                }
            }

            //if (validateIt)
            //{
            //    //validate packages
            //    List<Error> errors = validateStep(stepModelHelper.Model);
            //    if (errors != null)
            //        model.ErrorList = errors;
            //    else
            //        model.ErrorList = new List<Error>();

            //}

            model.StepInfo = stepInfo;

            return model;
        }

        private AbstractMetadataStepModel LoadSimpleAttributesForModelFromXml(StepModelHelper stepModelHelper)
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            var metadata = (XDocument)TaskManager.Bus[CreateTaskmanager.METADATA_XML];

            var complexElement = XmlUtility.GetXElementByXPath(stepModelHelper.XPath, metadata);
            var additionalyMetadataAttributeModel = new List<MetadataAttributeModel>();

            foreach (var simpleMetadataAttributeModel in stepModelHelper.Model.MetadataAttributeModels)
            {
                var numberOfSMM = 1;
                if (complexElement != null)
                {
                    //Debug.WriteLine("XXXXXXXXXXXXXXXXXXXX");
                    //Debug.WriteLine(simpleMetadataAttributeModel.Source.Label);
                    var childs = XmlUtility.GetChildren(complexElement).Where(e => e.Attribute("id").Value.Equals(simpleMetadataAttributeModel.Id.ToString()));

                    if (childs.Any())
                        numberOfSMM = childs.First().Elements().Count();
                }

                for (var i = 1; i <= numberOfSMM; i++)
                {
                    var xpath = stepModelHelper.GetXPathFromSimpleAttribute(simpleMetadataAttributeModel.Id, i);
                    var simpleElement = XmlUtility.GetXElementByXPath(xpath, metadata);

                    if (i == 1)
                    {
                        if (simpleElement != null && !String.IsNullOrEmpty(simpleElement.Value))
                        {
                            simpleMetadataAttributeModel.Value = simpleElement.Value;
                            // if at least on item has a value, the parent should be activated
                            setStepModelActive(stepModelHelper);
                        }
                    }
                    else
                    {
                        var newMetadataAttributeModel = simpleMetadataAttributeModel.Kopie(i, numberOfSMM);
                        newMetadataAttributeModel.Value = simpleElement.Value;
                        if (i == numberOfSMM) newMetadataAttributeModel.last = true;
                        additionalyMetadataAttributeModel.Add(newMetadataAttributeModel);
                    }
                }
            }

            foreach (var item in additionalyMetadataAttributeModel)
            {
                var tempList = stepModelHelper.Model.MetadataAttributeModels;

                var indexOfLastSameAttribute = tempList.IndexOf(tempList.Where(a => a.Id.Equals(item.Id)).Last());
                tempList.Insert(indexOfLastSameAttribute + 1, item);
            }

            return stepModelHelper.Model;
        }

        #endregion Models

        #region Security

        /// <summary>
        /// return true if user has edit rights
        /// </summary>
        /// <returns></returns>
        private bool hasUserEditAccessRights(long entityId)
        {
            FeaturePermissionManager featurePermissionManager = new FeaturePermissionManager();
            return featurePermissionManager.HasAccess(GetUsernameOrDefault(), typeof(User), null);
        }

        /// <summary>
        /// return true if user has edit rights
        /// </summary>
        /// <returns></returns>
        private bool hasUserEditRights(long entityId)
        {
            #region security permissions and authorisations check

            EntityPermissionManager entityPermissionManager = new EntityPermissionManager();
            return entityPermissionManager.HasRight(GetUsernameOrDefault(), typeof(User), "Dataset", entityId, RightType.Write);

            #endregion security permissions and authorisations check
        }

        #endregion Security

        #region overrideable Action

        public ActionResult Cancel()
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            if (TaskManager != null)
            {
                var dm = new DatasetManager();
                long datasetid = -1;
                long metadataStructureid = -1;
                var resetTaskManager = true;
                XmlDocument metadata = null;

                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.ENTITY_ID))
                {
                    datasetid = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.ENTITY_ID]);
                }

                if (datasetid > -1 && dm.IsDatasetCheckedIn(datasetid))
                {
                    var dataset = dm.GetDataset(datasetid);
                    metadataStructureid = dataset.MetadataStructure.Id;
                    metadata = dm.GetDatasetLatestMetadataVersion(datasetid);
                    TaskManager.UpdateBus(CreateTaskmanager.METADATA_XML, metadata);
                }

                return RedirectToAction("ImportMetadata", "Form", new { area = "Dcm", metadataStructureId = metadataStructureid, edit = false, created = true, locked = true });
            }

            return RedirectToAction("StartMetadataEditor", "Form");
        }

        public ActionResult Reset()
        {
            TaskManager = (CreateTaskmanager)Session["CreateDatasetTaskmanager"];
            if (TaskManager != null)
            {
                var dm = new DatasetManager();
                long datasetid = -1;
                long metadataStructureid = -1;
                var resetTaskManager = true;
                XmlDocument metadata = null;
                var edit = true;
                var created = false;

                if (TaskManager.Bus.ContainsKey(CreateTaskmanager.ENTITY_ID))
                {
                    datasetid = Convert.ToInt64(TaskManager.Bus[CreateTaskmanager.ENTITY_ID]);
                }

                if (datasetid > -1 && dm.IsDatasetCheckedIn(datasetid))
                {
                    var dataset = dm.GetDataset(datasetid);
                    metadataStructureid = dataset.MetadataStructure.Id;
                    metadata = dm.GetDatasetLatestMetadataVersion(datasetid);
                    TaskManager.UpdateBus(CreateTaskmanager.METADATA_XML, metadata);
                }

                return RedirectToAction("ImportMetadata", "Form", new { area = "Dcm", metadataStructureId = metadataStructureid, edit, created });
            }

            return RedirectToAction("StartMetadataEditor", "Form");
        }

        #endregion overrideable Action
    }
}