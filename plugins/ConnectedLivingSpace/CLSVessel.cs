﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ConnectedLivingSpace
{
    // A class that contains all the living space data for a particular vessel

    
    class CLSVessel
    {
        List<CLSPart> listParts;  // A list of parts in this vessel
        List<CLSSpace> listSpaces; // A list of seperate habitable spaces in this vessel.

        public CLSVessel()
        {
            listParts = new List<CLSPart>();
            listSpaces = new List<CLSSpace>();
        }

        public List<CLSSpace> Spaces
        {
            get
            {
                return listSpaces;
            }
        }

        public List<CLSPart> Parts
        {
            get
            {
                return listParts;
            }
        }

        internal void Populate(Vessel vessel)
        {
            Populate(vessel.rootPart);
        }

        internal void Populate(Part rootPart)
        {
            // Discard any currently held data.
            this.listParts.Clear();
            this.listSpaces.Clear();

            // Check that there is a root part, as if this was called in the EditorContext, the Editor.startPod will have been passed in, and that can be null.
            if (null != rootPart)
            {
                ProcessPart(rootPart, null); // This is called recursively going through the entire part tree.
                TidySpaces(); // This method might remove some of the spaces that have just been created if there are no habitable parts in them.
            }
        }

        // Method to go through each space once all the sapces are complete, and remove any that do not make sense, such as not having capacity for any crew.
        private void TidySpaces()
        {
            List<CLSSpace> listSpacesToRemove = new List<CLSSpace>();

            foreach(CLSSpace space in this.listSpaces)
            {
                if(space.MaxCrew == 0)
                {
                    listSpacesToRemove.Add(space);
                }
            }

            foreach(CLSSpace spaceToRemove in listSpacesToRemove)
            {
                spaceToRemove.Clear();
                listSpaces.Remove(spaceToRemove);
            }

        }

        // A method that is called recursively to walk the part tree, and allocate parts to habitable spaces
        private void ProcessPart(Part p, CLSSpace currentSpace , bool dockedToParent=false)
        {
            CLSSpace thisSpace = null;
            CLSPart newPart = new CLSPart(p);

            //Debug.Log("Processing part: " + p.name + " Navigable:"+newPart.Navigable + " Habitable:" + newPart.Habitable);

            // First add this part to the list of all parts for the vessel.
            this.listParts.Add(newPart);

            // Is the part capable of containing kerbals? If not then just att the part to the null space, but if it is then add it to the current space, or a new space if there is no current space.
            if (newPart.Navigable)
            {
                thisSpace = currentSpace;

                if (null == thisSpace)
                {
                    thisSpace = AddPartToNewSpace(newPart);
                }
                else
                {
                    thisSpace = AddPartToSpace(newPart, thisSpace);
                }
            }
            else
            {
                thisSpace = AddPartToSpace(newPart, null);
            }


            // Now loop through each of the part's children, consider if there is a navigable connection between them, and then make a recursive call.
            foreach (Part child in p.children)
            {
                // Get the attchment nodes
                AttachNode node = p.findAttachNodeByPart(child);
                AttachNode childNode = child.findAttachNodeByPart(p);
                bool dockingConnection = false;

                if (null == node && null == childNode) 
                {
                    // If there are no attachment nodes between the two parts, but they have a child parent relationship, then there is a chance that they are docked together. Consider that possibility.
                    {
                        Debug.Log("Considering if parts are docked together");
                        node = FindDockedNodeByPart(p, child);
                        childNode = FindDockedNodeByPart(child, p);
                        
                        // If we found both oif the nodes then we know that this connection is a docked connection. Let's just remember that, it might be useful to know later on.
                        if (null != node && null != childNode)
                        {
                            dockingConnection = true;
                        }
                    }
                }

                if (null != node && null != childNode) // TODO in the case of a surface attached part I believe that we will not have two attachemnt nodes, only one (or is it even posible to have none?) Test for this and work out what to do.
                {
                    // So do these two nodes together create a navigable passage?
                    if (IsNodeNavigable(node, p) && IsNodeNavigable(childNode, child))
                    {
                        ProcessPart(child, thisSpace, dockingConnection); // It looks like the connection is navigable. Process the child part and pass in the current space of this part.
                    }
                    else
                    {
                        ProcessPart(child, null); // There does not seem to be a way of Jeb accessing the child part. Sorry buddy - you will have to go EVA from here. Process the child part, bu pass in null. 
                    }
                }
                else
                {
                    ProcessPart(child, null); // There does not seem to be a way of Jeb accessing the child part. Sorry buddy - you will have to go EVA from here. Process the child part, bu pass in null. // TODO is this true?
                }

                // Was the connection a docking connection - if so we ought to mark the relevant CLSParts
                if (dockingConnection || dockedToParent)
                {
                    newPart.SetDocked(true);
                }
            }

        }

        // When parts are docked together it seems that their attachment nodes are not connected up. However since they are docked, they but must be docking ports, and each attachment node that is a docking port has a ModuleDockingNode associated with it, and each of those references an attachment node. This funciton finds the AttchNode referenced by the ModuleDockingNode that refers to another particular part
        private AttachNode FindDockedNodeByPart(Part thisPart, Part otherPart)
        {
            AttachNode thisNode = null;
            
            foreach (ModuleDockingNode docNode in thisPart.Modules.OfType<ModuleDockingNode>())
            {
                // TODO removed debugging
                Debug.Log("Part: " + thisPart.partInfo.title + " does have a ModuleDockingNode. Is it docked to " + otherPart.partInfo.title);
                Debug.Log("docNode.dockedPartUId = " + docNode.dockedPartUId);
                Debug.Log("otherPart.ConstructID = " + otherPart.ConstructID);

                if (otherPart == docNode.part.vessel[docNode.dockedPartUId])
                {
                    Debug.Log("Hopefully we have found the ModuleDockingNode that is docked to the art we are interested in.");
                    // This is the dockingNode that is docked to otherPart. Find the attachnode that it is associated with.
                    thisNode = thisPart.attachNodes.Find(n => n.id == docNode.referenceAttachNode);
                    if (null != thisNode)
                    {
                        break;
                    }
                    else
                    {
                        Debug.LogWarning("Failed to find an attachNode associated with a docked part.");
                    }
                }
            }

            return thisNode;
        }

        // Decides is an attachment node on a part could allow a kerbal to pass through it.
        private bool IsNodeNavigable(AttachNode node, Part p)
        {
            String passablenodes ="";
            String impassablenodes="";

            // Get the config for this part
            foreach (PartModule pm in p.Modules)
            {
                if (pm.moduleName == "ModuleConnectedLivingSpace")
                {
                    // This part does have a CLSmodule
                    ModuleConnectedLivingSpace CLSMod = (ModuleConnectedLivingSpace)pm;

                    passablenodes = CLSMod.passablenodes;
                    impassablenodes = CLSMod.impassablenodes;

                    break;
                }
            }

            if (passablenodes.Contains(node.id))
            {
                return true;
            }

            if (impassablenodes.Contains(node.id))
            {
                return false;
            }

            return true;
        }

        CLSSpace AddPartToNewSpace(CLSPart p)
        {
            CLSSpace newSpace = new CLSSpace(this);

            this.listSpaces.Add(newSpace);

            newSpace.AddPart(p);

            p.Space = newSpace;

            return newSpace;
        }

        CLSSpace AddPartToSpace(CLSPart p, CLSSpace space)
        {
            Debug.Log("AddPartToSpace " + ((Part)p).name);

            if(null !=space)
            {
                space.AddPart(p);
            }

            if(null != space)
            {
                p.Space = space;
            }

            return space;
        }

        // Method to throw away potential circular references before the object is disposed of
        public void Clear()
        {
            foreach (CLSSpace s in this.listSpaces)
            {
                s.Clear();
            }
            listSpaces.Clear();
        }

        // Method to highlight on unhighlight all the habitable spaces in this vessel
        public void Highlight(bool arg)
        {
            foreach (CLSSpace space in this.listSpaces)
            {
                space.Highlight(arg);
            }
        }
    }
}