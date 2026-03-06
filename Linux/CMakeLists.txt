cmake_minimum_required(VERSION 3.20)
project(BarkFluffQt VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

# Qt6
find_package(Qt6 REQUIRED COMPONENTS
    Core
    Widgets
    Network
    Svg
    Concurrent
    Multimedia
    MultimediaWidgets
    DBus
)

# KDE Frameworks (optional, for KConfig)
find_package(KF6 QUIET COMPONENTS Config CoreAddons)

# gRPC и Protobuf
find_package(Protobuf REQUIRED)
find_package(gRPC REQUIRED)

# OpenSSL
find_package(OpenSSL REQUIRED)

# === Proto files ===
set(PROTO_DIR "${CMAKE_SOURCE_DIR}/../BarkFluffBackend/Shared/BarkFluff.Proto")

set(PROTO_FILES
    ${PROTO_DIR}/shared.proto
    ${PROTO_DIR}/navigator_api.proto
    ${PROTO_DIR}/beacon_api.proto
    ${PROTO_DIR}/identity_api.proto
    ${PROTO_DIR}/fast_auth_api.proto
    ${PROTO_DIR}/users_api.proto
    ${PROTO_DIR}/messages_api.proto
    ${PROTO_DIR}/files_api.proto
    ${PROTO_DIR}/onliner_api.proto
    ${PROTO_DIR}/updates_api.proto
    ${PROTO_DIR}/configuration_api.proto
)

# Also generate Google's empty.proto
set(GOOGLE_PROTO_DIR "/usr/include")

# Generate protobuf and grpc files
set(PROTO_SRCS)
set(PROTO_HDRS)
set(GRPC_SRCS)
set(GRPC_HDRS)

foreach(PROTO_FILE ${PROTO_FILES})
    get_filename_component(PROTO_NAME ${PROTO_FILE} NAME_WE)
    
    list(APPEND PROTO_SRCS "${CMAKE_CURRENT_BINARY_DIR}/${PROTO_NAME}.pb.cc")
    list(APPEND PROTO_HDRS "${CMAKE_CURRENT_BINARY_DIR}/${PROTO_NAME}.pb.h")
    list(APPEND GRPC_SRCS "${CMAKE_CURRENT_BINARY_DIR}/${PROTO_NAME}.grpc.pb.cc")
    list(APPEND GRPC_HDRS "${CMAKE_CURRENT_BINARY_DIR}/${PROTO_NAME}.grpc.pb.h")
endforeach()

add_custom_command(
    OUTPUT ${PROTO_SRCS} ${PROTO_HDRS} ${GRPC_SRCS} ${GRPC_HDRS}
    COMMAND protobuf::protoc
    ARGS --cpp_out=${CMAKE_CURRENT_BINARY_DIR}
         --grpc_out=${CMAKE_CURRENT_BINARY_DIR}
         --plugin=protoc-gen-grpc=$<TARGET_FILE:gRPC::grpc_cpp_plugin>
         -I${PROTO_DIR}
         ${PROTO_FILES}
    DEPENDS ${PROTO_FILES}
    COMMENT "Generating protobuf/grpc sources"
    VERBATIM
)

# === Resource files ===
set(RESOURCES
    src/Resources/resources.qrc
)

# === Source files ===
set(SOURCES
    src/main.cpp
    
    # Connection
    src/Connection/GrpcClient.cpp
    src/Connection/NavigatorClient.cpp
    src/Connection/BeaconClient.cpp
    src/Connection/IdentityClient.cpp
    src/Connection/UsersClient.cpp
    src/Connection/FilesClient.cpp
    src/Connection/MessagesClient.cpp
    src/Connection/UpdatesClient.cpp
    src/Connection/OnlinerClient.cpp
    
    # Models
    src/Models/Chat.cpp
    
    # UI/Settings
    src/UI/Settings/SettingsPage.cpp
    src/UI/Settings/SessionsSettingsWidget.cpp
    src/UI/Settings/SecuritySettingsWidget.cpp
    src/UI/Settings/StorageSettingsWidget.cpp
    src/UI/Settings/AboutWidget.cpp
    src/UI/Settings/GeneralSettingsWidget.cpp
    
    # Shared Media Widgets
    src/UI/Widgets/SharedMediaGridView.cpp
    src/UI/Widgets/SharedDocumentsListView.cpp
    src/UI/Widgets/SharedMediaSection.cpp
    
    # Services
    src/Services/FileCacheService.cpp
    src/Services/SessionManager.cpp
    src/Services/NotificationService.cpp
    
    # Storage
    src/Storage/SecureStorage.cpp
    src/Storage/AppSettings.cpp
    
    # Utils
    src/Utils/DeviceIdGenerator.cpp
    src/Utils/SystemInfo.cpp
    src/Utils/ErrorHandler.cpp
    src/Utils/Validators.cpp
    src/Utils/ImageOptimizer.cpp
    
    # UI
    src/UI/MainWindow.cpp
    src/UI/ServerSelectPage.cpp
    src/UI/LoginPage.cpp
    src/UI/RegisterPage.cpp
    src/UI/MessengerPage.cpp
    src/UI/PinUnlockDialog.cpp
    src/UI/ProfilePage.cpp
    
    # UI/Widgets
    src/UI/Widgets/AttachmentWidgets.cpp
    src/UI/Widgets/EmojiPicker.cpp
    src/UI/Widgets/PinCodeInputWidget.cpp
    src/UI/Widgets/MessageBubbleShape.cpp
    src/UI/Widgets/ScrollToBottomButton.cpp
    src/UI/Widgets/OnlineStatusWidget.cpp
    src/UI/Widgets/AvatarWidget.cpp
    src/UI/Widgets/BadgeChipWidget.cpp
    src/UI/Widgets/FlowLayout.cpp
    src/UI/Widgets/ExpandingTextEdit.cpp
    
    # UI/Views
    src/UI/UserProfileView.cpp
)

set(HEADERS
    # Connection
    src/Connection/GrpcClient.h
    src/Connection/NavigatorClient.h
    src/Connection/BeaconClient.h
    src/Connection/IdentityClient.h
    src/Connection/UsersClient.h
    src/Connection/FilesClient.h
    src/Connection/MessagesClient.h
    src/Connection/UpdatesClient.h
    src/Connection/OnlinerClient.h
    
    # Models
    src/Models/ServerInfo.h
    src/Models/ServerConfiguration.h
    src/Models/UserSession.h
    src/Models/DeviceMetadata.h
    src/Models/RegistrationState.h
    src/Models/Chat.h
    src/Models/ChatMember.h
    src/Models/Message.h
    src/Models/MessageContent.h
    src/Models/SelectedAttachment.h
    src/Models/User.h
    src/Models/UsernameValidationStatus.h
    src/Models/SharedMediaItem.h
    src/Models/SessionInfo.h
    src/Models/NotificationSettings.h
    
    # UI/Settings
    src/UI/Settings/SettingsCategory.h
    src/UI/Settings/SettingsPage.h
    src/UI/Settings/SessionsSettingsWidget.h
    src/UI/Settings/SecuritySettingsWidget.h
    src/UI/Settings/StorageSettingsWidget.h
    src/UI/Settings/AboutWidget.h
    src/UI/Settings/GeneralSettingsWidget.h
    
    # Services
    src/Services/FileCacheService.h
    src/Services/SessionManager.h
    src/Services/NotificationService.h
    
    # Storage
    src/Storage/SecureStorage.h
    src/Storage/AppSettings.h
    
    # Utils
    src/Utils/DeviceIdGenerator.h
    src/Utils/SystemInfo.h
    src/Utils/ErrorHandler.h
    src/Utils/Validators.h
    src/Utils/MessageGrouper.h
    src/Utils/JwtUtils.h
    src/Utils/ImageOptimizer.h
    
    # UI
    src/UI/MainWindow.h
    src/UI/ServerSelectPage.h
    src/UI/LoginPage.h
    src/UI/RegisterPage.h
    src/UI/MessengerPage.h
    src/UI/PinUnlockDialog.h
    src/UI/ProfilePage.h
    
    # UI/Widgets
    src/UI/Widgets/AttachmentWidgets.h
    src/UI/Widgets/EmojiPicker.h
    src/UI/Widgets/PinCodeInputWidget.h
    src/UI/Widgets/MessageBubbleShape.h
    src/UI/Widgets/ScrollToBottomButton.h
    src/UI/Widgets/OnlineStatusWidget.h
    src/UI/Widgets/AvatarWidget.h
    src/UI/Widgets/BadgeChipWidget.h
    src/UI/Widgets/FlowLayout.h
    src/UI/Widgets/ExpandingTextEdit.h
    src/UI/Widgets/SharedMediaGridView.h
    src/UI/Widgets/SharedDocumentsListView.h
    src/UI/Widgets/SharedMediaSection.h
    
    # UI/Views
    src/UI/UserProfileView.h
)

# === Executable ===
add_executable(${PROJECT_NAME}
    ${SOURCES}
    ${HEADERS}
    ${RESOURCES}
    ${PROTO_SRCS}
    ${GRPC_SRCS}
)

# Include directories
target_include_directories(${PROJECT_NAME} PRIVATE
    ${CMAKE_CURRENT_BINARY_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/src
)

# Link libraries
target_link_libraries(${PROJECT_NAME} PRIVATE
    Qt6::Core
    Qt6::Widgets
    Qt6::Network
    Qt6::Svg
    Qt6::Concurrent
    Qt6::Multimedia
    Qt6::MultimediaWidgets
    Qt6::DBus
    protobuf::libprotobuf
    gRPC::grpc++
    gRPC::grpc++_reflection
    OpenSSL::SSL
    OpenSSL::Crypto
)

# KF6 Config (optional)
if(KF6_FOUND)
    target_link_libraries(${PROJECT_NAME} PRIVATE KF6::ConfigCore)
    target_compile_definitions(${PROJECT_NAME} PRIVATE USE_KCONFIG)
endif()

# Install
install(TARGETS ${PROJECT_NAME}
    RUNTIME DESTINATION bin
)