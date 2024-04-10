/**
 * 2S-API
 * The 2S-API is a RESTful API for interacting with the 2S Platform.
 *
 * The version of the OpenAPI document: 0.8.26.4237
 * Contact: kobr.ales@outlook.com
 *
 * NOTE: This class is auto generated by OpenAPI Generator (https://openapi-generator.tech).
 * https://openapi-generator.tech
 * Do not edit the class manually.
 */

import { RequestFile } from './models';

export class MachineAccess {
    'id'?: string;
    'objectId'?: string;
    'accessKey'?: string;
    'scope'?: MachineAccess.ScopeEnum = MachineAccess.ScopeEnum.AnnotationRead;
    'annotationKey'?: string;

    static discriminator: string | undefined = undefined;

    static attributeTypeMap: Array<{name: string, baseName: string, type: string}> = [
        {
            "name": "id",
            "baseName": "Id",
            "type": "string"
        },
        {
            "name": "objectId",
            "baseName": "ObjectId",
            "type": "string"
        },
        {
            "name": "accessKey",
            "baseName": "AccessKey",
            "type": "string"
        },
        {
            "name": "scope",
            "baseName": "Scope",
            "type": "MachineAccess.ScopeEnum"
        },
        {
            "name": "annotationKey",
            "baseName": "AnnotationKey",
            "type": "string"
        }    ];

    static getAttributeTypeMap() {
        return MachineAccess.attributeTypeMap;
    }
}

export namespace MachineAccess {
    export enum ScopeEnum {
        AnnotationRead = <any> 'AnnotationRead',
        ConfigurationRead = <any> 'ConfigurationRead',
        DefaultRead = <any> 'DefaultRead',
        AnnotationReadWrite = <any> 'AnnotationReadWrite',
        ConfigurationReadWrite = <any> 'ConfigurationReadWrite',
        DefaultReadWrite = <any> 'DefaultReadWrite'
    }
}
