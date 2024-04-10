export * from './accessApi';
import { AccessApi } from './accessApi';
export * from './annotationsApi';
import { AnnotationsApi } from './annotationsApi';
export * from './configurationApi';
import { ConfigurationApi } from './configurationApi';
import * as http from 'http';

export class HttpError extends Error {
    constructor (public response: http.IncomingMessage, public body: any, public statusCode?: number) {
        super('HTTP request failed');
        this.name = 'HttpError';
    }
}

export { RequestFile } from '../model/models';

export const APIS = [AccessApi, AnnotationsApi, ConfigurationApi];
