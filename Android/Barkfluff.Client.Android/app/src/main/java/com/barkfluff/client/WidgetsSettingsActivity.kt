package com.barkfluff.client

import android.appwidget.AppWidgetManager
import android.content.Intent
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.appcompat.app.AppCompatActivity
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ActivityWidgetsSettingsBinding
import com.barkfluff.client.databinding.ItemWidgetConfigBinding
import com.barkfluff.client.widget.WidgetConfig
import com.barkfluff.client.widget.WidgetConfigureActivity
import com.barkfluff.client.widget.WidgetRepository

/**
 * Экран "Виджеты" в настройках. Показывает список созданных виджетов
 * (тапом — редактирование через WidgetConfigureActivity в edit-mode) и подсказку
 * "Чтобы добавить виджет, удерживайте палец на рабочем столе → BarkFluff".
 */
class WidgetsSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityWidgetsSettingsBinding
    private lateinit var adapter: WidgetsAdapter

    private var configs: List<Pair<Int, WidgetConfig>> = emptyList()
    private var placedIds: Set<Int> = emptySet()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityWidgetsSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        binding.toolbar.setNavigationOnClickListener { finish() }

        adapter = WidgetsAdapter()
        binding.widgetsRecyclerView.layoutManager = LinearLayoutManager(this)
        binding.widgetsRecyclerView.adapter = adapter
    }

    override fun onResume() {
        super.onResume()
        reload()
    }

    private fun reload() {
        // Чистим конфиги для виджетов, которых уже нет на экране (на случай неуспевшего onDeleted)
        val placed = WidgetRepository.placedAppWidgetIds(this).toSet()
        placedIds = placed

        val all = WidgetRepository.listAllConfigs(this)
        // Удаляем "висячие" конфиги для несуществующих widget'ов
        for ((id, _) in all) {
            if (id !in placed) {
                WidgetRepository.deleteConfig(this, id)
            }
        }
        configs = WidgetRepository.listAllConfigs(this)
        adapter.notifyDataSetChanged()
        binding.emptyText.visibility = if (configs.isEmpty()) View.VISIBLE else View.GONE
    }

    private inner class WidgetsAdapter : RecyclerView.Adapter<WidgetsAdapter.VH>() {

        override fun getItemCount(): Int = configs.size

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
            val b = ItemWidgetConfigBinding.inflate(
                LayoutInflater.from(parent.context), parent, false
            )
            return VH(b)
        }

        override fun onBindViewHolder(holder: VH, position: Int) {
            val (id, cfg) = configs[position]
            holder.bind(id, cfg)
        }

        inner class VH(val b: ItemWidgetConfigBinding) : RecyclerView.ViewHolder(b.root) {
            fun bind(appWidgetId: Int, cfg: WidgetConfig) {
                b.widgetName.text = cfg.name.ifBlank { getString(R.string.widget_default_name) }
                b.widgetSubtitle.text = getString(R.string.widget_chats_count, cfg.chatIds.size)
                b.root.setOnClickListener {
                    val intent = Intent(this@WidgetsSettingsActivity, WidgetConfigureActivity::class.java).apply {
                        putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId)
                        putExtra(WidgetConfigureActivity.EXTRA_EDIT_MODE, true)
                    }
                    startActivity(intent)
                }
            }
        }
    }
}
