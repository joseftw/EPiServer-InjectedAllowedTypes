﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EPiServer.Core;
using EPiServer.DataAbstraction.RuntimeModel;
using EPiServer.DataAnnotations;

namespace JOS.InjectedAllowedTypes
{
    public class InjectedContentDataAttributeScanningAssigner : ContentDataAttributeScanningAssigner
    {
        /// <summary>
        /// Almost exact implementation of the AssignValuesToPropertyDefinition in the ContentDataAttributeScanningAssigner
        /// the only thing that differs is the added call to CustomAllowedTypes.GetMergedAllowedTypesAttribute.
        /// That call allows us to add more types to the Allowed/RestricedTypes without using the AllowedTypes attribute.
        /// </summary>
        /// <param name="propertyDefinitionModel"></param>
        /// <param name="property"></param>
        /// <param name="parentModel"></param>
        public override void AssignValuesToPropertyDefinition(PropertyDefinitionModel propertyDefinitionModel, PropertyInfo property, ContentTypeModel parentModel)
        {
            if (property.IsAutoGenerated() && !property.IsAutoVirtualPublic())
            {
                var exceptionMessage = string.Format(CultureInfo.InvariantCulture,
                    "The property '{0}' on the content type '{1}' is autogenerated but not virtual declared.",
                    property.Name, property.DeclaringType.Name);
                throw new InvalidOperationException(exceptionMessage);
            }

            //This is our added logic to merge a predefined AllowedTypes attribute with our own AllowedTypes specified in code.
            #region ModularAllowedTypes
            var customAttributes = Attribute.GetCustomAttributes(property, true).ToList();
            var injectedAllowedTypesAttribute = InjectedAllowedTypes.GetInjectedAllowedTypesAttribute(parentModel.ModelType,
                    property.Name);
            var specifiedAllowedTypesAttribute = property.GetCustomAttribute<InjectedAllowedTypesAttribute>();

            //We DO NOT include an existing AllowedTypesAttribute in the merge, because if the AllowedTypesAttribute is used, EPiServer will ONLY
            //look at the AllowedTypesAttribute thus making the validation fail. We can't hook into that method as far as I know so you will need
            //to use the InjectedAllowedTypesAttribute instead of the AllowedTypesAttribute.
            if (customAttributes.Any(x => x is AllowedTypesAttribute))
            {
                var existingAllowedTypesAttribute =
                    customAttributes.FirstOrDefault(x => x is AllowedTypesAttribute) as AllowedTypesAttribute;

                if (injectedAllowedTypesAttribute != null)
                {
                    var mergedAllowedTypesAttribute = InjectedAllowedTypes.MergeAttributes(injectedAllowedTypesAttribute, specifiedAllowedTypesAttribute);
                    customAttributes.Remove(existingAllowedTypesAttribute);
                    customAttributes.Add(mergedAllowedTypesAttribute);
                }
            }
            else
            {
                var mergedAllowedTypesAttribute = InjectedAllowedTypes.MergeAttributes(injectedAllowedTypesAttribute, specifiedAllowedTypesAttribute);

                if (mergedAllowedTypesAttribute != null)
                {
                    customAttributes.Add(mergedAllowedTypesAttribute);
                }
            }
            #endregion

            foreach (var attribute in customAttributes)
            {
                if (attribute is BackingTypeAttribute)
                {
                    var backingTypeAttribute = attribute as BackingTypeAttribute;

                    if (backingTypeAttribute.BackingType != null)
                    {
                        if (!typeof(PropertyData).IsAssignableFrom(backingTypeAttribute.BackingType))
                        {
                            var exceptionMessage = string.Format(CultureInfo.InvariantCulture,
                                "The backing type '{0}' attributed to the property '{1}' on '{2}' does not inherit PropertyData.",
                                backingTypeAttribute.BackingType.FullName, property.Name, property.DeclaringType.Name);
                            throw new TypeMismatchException(exceptionMessage);
                        }

                        if (property.IsAutoVirtualPublic())
                        {
                            ValidateTypeCompability(property, backingTypeAttribute.BackingType);
                        }
                    }
                    propertyDefinitionModel.BackingType = backingTypeAttribute.BackingType;
                }
                else if (attribute is AllowedTypesAttribute)
                {
                    var allowedTypesAttribute = attribute as AllowedTypesAttribute;
                    VerifyAllowedTypesAttribute(allowedTypesAttribute, property);
                }
                else if (attribute is DisplayAttribute)
                {
                    var displayAttribute = attribute as DisplayAttribute;
                    propertyDefinitionModel.DisplayName = displayAttribute.GetName();
                    propertyDefinitionModel.Description = displayAttribute.GetDescription();
                    propertyDefinitionModel.Order = displayAttribute.GetOrder();
                    propertyDefinitionModel.TabName = displayAttribute.GetGroupName();
                }
                else if (attribute is ScaffoldColumnAttribute)
                {
                    var scaffoldColumnAttribute = attribute as ScaffoldColumnAttribute;
                    propertyDefinitionModel.AvailableInEditMode = scaffoldColumnAttribute.Scaffold;
                }
                else if (attribute is CultureSpecificAttribute)
                {
                    var specificAttribute = attribute as CultureSpecificAttribute;
                    ThrowIfBlockProperty(specificAttribute, property);
                    propertyDefinitionModel.CultureSpecific = specificAttribute.IsCultureSpecific;
                }
                else if (attribute is RequiredAttribute)
                {
                    var requiredAttribute = attribute as RequiredAttribute;
                    ThrowIfBlockProperty(requiredAttribute, property);
                    propertyDefinitionModel.Required = true;
                }
                else if (attribute is SearchableAttribute)
                {
                    var searchableAttribute = attribute as SearchableAttribute;
                    ThrowIfBlockProperty(searchableAttribute, property);
                    propertyDefinitionModel.Searchable = searchableAttribute.IsSearchable;
                }
                else if (attribute is UIHintAttribute)
                {
                    var uiHintAttribute = attribute as UIHintAttribute;
                    if (!string.IsNullOrEmpty(uiHintAttribute.UIHint))
                    {
                        if (string.Equals(uiHintAttribute.PresentationLayer, "website"))
                        {
                            propertyDefinitionModel.TemplateHint = uiHintAttribute.UIHint;
                        }
                        else if (string.IsNullOrEmpty(uiHintAttribute.PresentationLayer) &&
                                 string.IsNullOrEmpty(propertyDefinitionModel.TemplateHint))
                        {
                            propertyDefinitionModel.TemplateHint = uiHintAttribute.UIHint;
                        }
                    }
                }

                propertyDefinitionModel.Attributes.AddAttribute(attribute);
            }
        }

        //Calls the VerifyAllowedTypesAttribute in the parent class with reflection.
        //NOTE: Don't rename this method since the name of the method is used to call the parent method.
        private static void VerifyAllowedTypesAttribute(AllowedTypesAttribute attribute, PropertyInfo property)
        {
            var methodName = MethodBase.GetCurrentMethod().Name;
            var method = GetMethodFromParent(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            var parameters = new object[] {attribute, property};
            method.Invoke(null, parameters);
        }

        //Calls the ValidateTypeCompability in the parent class with reflection.
        //NOTE: Don't rename this method since the name of the method is used to call the parent method.
        private static void ValidateTypeCompability(PropertyInfo property, Type backingType)
        {
            var methodName = MethodBase.GetCurrentMethod().Name;
            var method = GetMethodFromParent(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            var parameters = new object[] {property, backingType};
            method.Invoke(null, parameters);
        }

        //Calls the ThrowIfBlockProperty in the parent class with reflection.
        //NOTE: Don't rename this method since the name of the method is used to call the parent method.
        private static void ThrowIfBlockProperty(Attribute attribute, PropertyInfo property)
        {
            var methodName = MethodBase.GetCurrentMethod().Name;
            var method = GetMethodFromParent(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            var parameters = new object[] {attribute, property};
            method.Invoke(null, parameters);
        }

        private static MethodInfo GetMethodFromParent(string methodName, BindingFlags bindingFlags)
        {
            var type = typeof (ContentDataAttributeScanningAssigner);
            var method = type.GetMethod(methodName, bindingFlags);

            if (method == null)
            {
                var exceptionMessage =
                    string.Format(
                        "Couldn't find the {0} method in EPiServer class {1}. Maybe it has been renamed/removed?",
                        methodName, type.FullName);

                throw new MissingMethodException(exceptionMessage);
            }

            return method;
        }
    }
}