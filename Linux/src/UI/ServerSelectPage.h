#pragma once

#include <QWidget>
#include <QListWidget>
#include <QPushButton>
#include <QLabel>
#include <QProgressBar>
#include "Models/ServerInfo.h"
#include "Models/ServerConfiguration.h"
#include "Connection/NavigatorClient.h"

namespace BarkFluff {

class ServerSelectPage : public QWidget {
    Q_OBJECT
    
public:
    explicit ServerSelectPage(QWidget* parent = nullptr);
    
signals:
    void serverSelected(const ServerConfiguration& config);
    
private slots:
    void onRefreshServers();
    void onServerDoubleClicked(QListWidgetItem* item);
    void onConnectClicked();
    void onAddCustomServer();
    
private:
    void setupUi();
    void loadServers();
    void updateServerList(const QList<ServerInfo>& servers);
    void connectToServer(const ServerInfo& server);
    
    QListWidget* serverList_;
    QPushButton* refreshButton_;
    QPushButton* connectButton_;
    QPushButton* addCustomButton_;
    QProgressBar* loadingBar_;
    QLabel* statusLabel_;
    
    QList<ServerInfo> servers_;
    NavigatorClient* navigatorClient_;
};

} // namespace BarkFluff