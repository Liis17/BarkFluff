#include "ServerSelectPage.h"
#include "Connection/BeaconClient.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QInputDialog>
#include <QMessageBox>
#include <QtConcurrent>

namespace BarkFluff {

ServerSelectPage::ServerSelectPage(QWidget* parent)
    : QWidget(parent)
    , navigatorClient_(new NavigatorClient())
{
    setupUi();
    loadServers();
}

void ServerSelectPage::setupUi() {
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(20, 20, 20, 20);
    layout->setSpacing(15);
    
    // Title
    auto* titleLabel = new QLabel(tr("Select Server"), this);
    titleLabel->setStyleSheet("font-size: 24px; font-weight: bold;");
    titleLabel->setAlignment(Qt::AlignCenter);
    layout->addWidget(titleLabel);
    
    // Server list
    serverList_ = new QListWidget(this);
    serverList_->setStyleSheet(R"(
        QListWidget {
            border: 1px solid #ccc;
            border-radius: 8px;
            background: #fff;
        }
        QListWidget::item {
            padding: 15px;
            border-bottom: 1px solid #eee;
        }
        QListWidget::item:selected {
            background: #3DAEE9;
            color: white;
        }
    )");
    connect(serverList_, &QListWidget::itemDoubleClicked, this, &ServerSelectPage::onServerDoubleClicked);
    layout->addWidget(serverList_);
    
    // Loading bar
    loadingBar_ = new QProgressBar(this);
    loadingBar_->setRange(0, 0);  // Indeterminate
    loadingBar_->setTextVisible(false);
    loadingBar_->setMaximumHeight(4);
    loadingBar_->hide();
    layout->addWidget(loadingBar_);
    
    // Status label
    statusLabel_ = new QLabel(this);
    statusLabel_->setAlignment(Qt::AlignCenter);
    statusLabel_->setStyleSheet("color: #666;");
    layout->addWidget(statusLabel_);
    
    // Buttons
    auto* buttonLayout = new QHBoxLayout();
    
    refreshButton_ = new QPushButton(tr("Refresh"), this);
    refreshButton_->setStyleSheet("padding: 10px 20px;");
    connect(refreshButton_, &QPushButton::clicked, this, &ServerSelectPage::onRefreshServers);
    buttonLayout->addWidget(refreshButton_);
    
    connectButton_ = new QPushButton(tr("Connect"), this);
    connectButton_->setStyleSheet("padding: 10px 20px; background: #3DAEE9; color: white;");
    connectButton_->setEnabled(false);
    connect(connectButton_, &QPushButton::clicked, this, &ServerSelectPage::onConnectClicked);
    buttonLayout->addWidget(connectButton_);
    
    layout->addLayout(buttonLayout);
    
    // Add custom server button
    addCustomButton_ = new QPushButton(tr("+ Add Custom Server"), this);
    addCustomButton_->setFlat(true);
    addCustomButton_->setStyleSheet("color: #3DAEE9; padding: 5px;");
    connect(addCustomButton_, &QPushButton::clicked, this, &ServerSelectPage::onAddCustomServer);
    layout->addWidget(addCustomButton_);
    
    // Enable connect button when item selected
    connect(serverList_, &QListWidget::itemSelectionChanged, this, [this]() {
        connectButton_->setEnabled(serverList_->currentRow() >= 0);
    });
}

void ServerSelectPage::loadServers() {
    loadingBar_->show();
    statusLabel_->setText(tr("Loading servers..."));
    refreshButton_->setEnabled(false);
    
    QtConcurrent::run([this]() {
        try {
            auto servers = navigatorClient_->listServers();
            QMetaObject::invokeMethod(this, [this, servers]() {
                servers_ = servers;
                updateServerList(servers);
                loadingBar_->hide();
                statusLabel_->setText(tr("Found %1 server(s)").arg(servers.size()));
                refreshButton_->setEnabled(true);
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                loadingBar_->hide();
                statusLabel_->setText(tr("Error: %1").arg(msg));
                refreshButton_->setEnabled(true);
            });
        }
    });
}

void ServerSelectPage::updateServerList(const QList<ServerInfo>& servers) {
    serverList_->clear();
    
    for (const auto& server : servers) {
        auto* item = new QListWidgetItem(serverList_);
        
        QString text = QString("%1\n%2 | 👥 %3")
            .arg(server.name)
            .arg(server.location)
            .arg(server.accountsCount);
        
        item->setText(text);
        item->setData(Qt::UserRole, QVariant::fromValue(server.name));
        
        // Set color indicator
        if (!server.color.mainHex.isEmpty()) {
            QColor color(server.color.mainHex);
            item->setBackground(color.lighter(170));
        }
    }
}

void ServerSelectPage::onRefreshServers() {
    loadServers();
}

void ServerSelectPage::onServerDoubleClicked(QListWidgetItem* item) {
    int index = serverList_->row(item);
    if (index >= 0 && index < servers_.size()) {
        connectToServer(servers_[index]);
    }
}

void ServerSelectPage::onConnectClicked() {
    int index = serverList_->currentRow();
    if (index >= 0 && index < servers_.size()) {
        connectToServer(servers_[index]);
    }
}

void ServerSelectPage::connectToServer(const ServerInfo& server) {
    loadingBar_->show();
    statusLabel_->setText(tr("Connecting to %1...").arg(server.name));
    setEnabled(false);
    
    QtConcurrent::run([this, server]() {
        try {
            BeaconClient beacon(server.beaconUri.host, server.beaconUri.port);
            auto config = beacon.getServerInfo();
            
            // Store beacon endpoint for reconnection
            config.beaconEndpoint = server.beaconUri;
            
            QMetaObject::invokeMethod(this, [this, config]() {
                loadingBar_->hide();
                setEnabled(true);
                emit serverSelected(config);
            });
        } catch (const std::exception& e) {
            QMetaObject::invokeMethod(this, [this, msg = QString::fromStdString(e.what())]() {
                loadingBar_->hide();
                statusLabel_->setText(tr("Connection failed: %1").arg(msg));
                setEnabled(true);
            });
        }
    });
}

void ServerSelectPage::onAddCustomServer() {
    bool ok;
    QString address = QInputDialog::getText(this, tr("Add Custom Server"),
        tr("Enter server address (host:port):"), QLineEdit::Normal, "", &ok);
    
    if (ok && !address.isEmpty()) {
        QStringList parts = address.split(':');
        if (parts.size() == 2) {
            ServerInfo custom;
            custom.name = address;
            custom.beaconUri.host = parts[0];
            custom.beaconUri.port = parts[1].toInt();
            custom.location = tr("Custom");
            custom.accountsCount = 0;
            
            servers_.append(custom);
            updateServerList(servers_);
        } else {
            QMessageBox::warning(this, tr("Invalid Address"), 
                tr("Please enter address in format: host:port"));
        }
    }
}

} // namespace BarkFluff