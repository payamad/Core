﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;
using BExIS.Dcm.Wizard;

namespace BExIS.Web.Shell.Areas.DCM.Models.ImportMetadata
{
    public class ReadSourceModel:AbstractStepModel
    {
        
        [Display(Name = "Schema name")]
        public string SchemaName { get; set; }

        [Display(Name = "Root node" ) ]
        public string RootNode { get; set; }

        public ReadSourceModel(StepInfo stepInfo)
        { 
            this.StepInfo = stepInfo;
            RootNode = "";
            SchemaName = "";
        }
    }
}