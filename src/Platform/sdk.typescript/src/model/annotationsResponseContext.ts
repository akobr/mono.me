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
import { Context } from './context';

export class AnnotationsResponseContext {
    'annotations'?: Array<Context>;
    'continuationToken'?: string;
    'count'?: number;

    static discriminator: string | undefined = undefined;

    static attributeTypeMap: Array<{name: string, baseName: string, type: string}> = [
        {
            "name": "annotations",
            "baseName": "Annotations",
            "type": "Array<Context>"
        },
        {
            "name": "continuationToken",
            "baseName": "ContinuationToken",
            "type": "string"
        },
        {
            "name": "count",
            "baseName": "Count",
            "type": "number"
        }    ];

    static getAttributeTypeMap() {
        return AnnotationsResponseContext.attributeTypeMap;
    }
}

