// Entry point for esbuild — aggregates all generated proto stubs into window.barkfluff
// Generated stubs populate window.proto via goog.exportSymbol automatically
const grpcWeb = require('grpc-web');
require('./shared_pb.js');
require('./identity_api_pb.js');
require('./users_api_pb.js');
require('./messages_api_pb.js');
require('./files_api_pb.js');
require('./updates_api_pb.js');
require('./onliner_api_pb.js');
const identitySvc = require('./identity_api_grpc_web_pb.js');
const usersSvc    = require('./users_api_grpc_web_pb.js');
const messagesSvc = require('./messages_api_grpc_web_pb.js');
const filesSvc    = require('./files_api_grpc_web_pb.js');
const updatesSvc  = require('./updates_api_grpc_web_pb.js');
const onlinerSvc  = require('./onliner_api_grpc_web_pb.js');

window.barkfluff = {
    grpcWeb,
    IdentityApiClient:        identitySvc.IdentityApiClient,
    IdentityApiPromiseClient: identitySvc.IdentityApiPromiseClient,
    UsersApiClient:           usersSvc.UsersApiClient,
    UsersApiPromiseClient:    usersSvc.UsersApiPromiseClient,
    MessagesApiClient:        messagesSvc.MessagesApiClient,
    MessagesApiPromiseClient: messagesSvc.MessagesApiPromiseClient,
    FilesApiClient:           filesSvc.FilesApiClient,
    FilesApiPromiseClient:    filesSvc.FilesApiPromiseClient,
    UpdatesApiClient:         updatesSvc.UpdatesApiClient,
    UpdatesApiPromiseClient:  updatesSvc.UpdatesApiPromiseClient,
    OnlinerApiClient:         onlinerSvc.OnlinerApiClient,
    OnlinerApiPromiseClient:  onlinerSvc.OnlinerApiPromiseClient,
};
